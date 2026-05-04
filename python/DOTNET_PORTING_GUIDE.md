# .NET Porting Guide for AI Agents

この文書は、Python試作版 `PlcIoChecker_QR` を .NET 版へ作り直すAI/開発者向けの仕様メモです。
Python版は動作確認用です。.NET版では、スマホで作ったJSONの編集、QR生成、JSON保存まで本格的に扱います。

## 目的

- PCでPLC IO Checker用の案件設定を作成・編集する。
- スマホ側の手入力を減らすため、設定をQRでAndroid/iOSへ渡す。
- ネットワーク転送は使わず、まずQRを正式ルートにする。
- スマホでエクスポートしたJSONもPCで読み込み、編集して再度JSON保存またはQR表示できるようにする。

## 対象データ

スマホアプリの通常JSONインポートと同じ `ProjectDefinition` 相当を扱う。

主な項目:

- `id`
- `name`
- `connection`
- `devices`
- `watchItems`
- `traps`
- `settings`
- `updatedAtEpochMs`

`devices` は最低限 `address` と `dataType` を保存する。コメント、種別、vendor、watch状態はスマホ側でhydrateされる設計に合わせる。
`watchItems` はタイムチャート対象アドレスの配列。
`traps` はトラップ条件を保存する。

## JSON方針

- PC保存用JSONは整形済みでよい。人が確認しやすいことを優先する。
- QR用JSONは最小化する。`separators=(",", ":")` 相当で余計な空白を入れない。
- `sha256` は圧縮前の最小化JSON bytesに対して計算する。
- 余計な互換fallbackや別名変換は作らない。バグに気づけなくなるため。
- 不正な値はPC側GUIで選べないようにする。読み込み時に不正値があれば明示的にエラーにする。

## QR形式

現行形式は `PLCIOC2D` のみ。

```text
PLCIOC2D|<session>|<index>|<total>|<sha256>|<payload-chunk>
```

- `session`: 12文字程度のランダムID。1セットのQRを識別する。
- `index`: 1始まり。
- `total`: QR総枚数。
- `sha256`: 圧縮前の最小化JSON bytesのSHA-256 hex。
- `payload-chunk`: raw deflate済みbytesをbase64url化し、paddingの `=` を除いた文字列の一部。

禁止:

- `PLCIOC1` を出さない。
- `PLCIOC2Z` を出さない。
- 旧形式を自動変換しない。
- zlib container形式を使わない。

## 圧縮仕様

必ず raw deflate を使う。

Pythonでは以下に相当する。

```python
compressor = zlib.compressobj(level=9, wbits=-15)
compressed = compressor.compress(json_bytes) + compressor.flush()
```

.NETでは `System.IO.Compression.DeflateStream` を使う想定。`ZLibStream` やgzip形式は使わない。
実装後は、生成したpayloadをAndroid/iOSで読めること、また圧縮前JSONのSHA-256が一致することを必ず確認する。

重要な学び:

- Pythonの通常 `zlib.compress()` はzlib containerヘッダ付きになる。
- Androidは読める場合があるが、iOS `Compression.COMPRESSION_ZLIB` では期待通りに復元できなかった。
- その結果、QR自体は検出できるが、チェックサム不一致で「QR読込失敗」になる。
- このため `PLCIOC2D` は raw deflate 前提の形式として固定する。

## base64url仕様

- 通常Base64から `+` を `-`、`/` を `_` に置換する。
- paddingの `=` は削除する。
- 復元時は文字数に応じて `=` を戻す。
- 文字列はQRの `payload-chunk` として分割する。

## QR分割

デフォルト:

- `QR 1枚の文字数`: `350`
- `QR表示サイズpx`: `650`
- `QR誤り訂正`: `L 読取優先`

理由:

- 1枚あたりの文字数を増やすとQR枚数は減る。
- ただしiOS実機では密度が高いQRが不安定になる場合がある。
- 読めない大きい1枚QRより、読める複数枚QRを優先する。

許容設定:

- 読取優先: `200` または `350`
- 枚数優先: `700` 以上。ただし端末で読めることを確認してから使う。

## .NET UIに必要な画面

最小版:

- 案件名
- Vendor: `Melsec`, `Keyence`
- 接続モード: `Real`, `DemoMock`
- IP
- Port
- CPU機種
- KEYENCE表示: `Normal`, `Xym`
- Transport: `Tcp`, `Udp`
- 監視周期ms
- Timeout ms
- Network
- Station
- Module IO
- Multidrop
- デバイス一覧
- タイムチャート対象一覧
- トラップ一覧
- QR 1枚の文字数
- QR表示サイズ
- QR誤り訂正
- JSONプレビュー
- QR本文プレビュー
- QRページ移動

ボタン:

- 新規
- JSON読込
- JSON保存
- QR生成
- QR PNG保存

将来版:

- スマホで作成したJSONを読み込んで編集する。
- デバイス登録、タイムチャート登録、トラップ登録を表形式で編集する。
- 値候補はcombo boxなどで固定し、自由入力による不正値を減らす。
- JSON schemaに近い検証を入れて、保存前に問題を出す。

## Enumと入力値

`TrapCondition` は以下だけを許可する。

- `Rise`
- `Fall`
- `Change`
- `GreaterOrEqual`
- `LessOrEqual`
- `Equal`
- `NotEqual`

禁止:

- `Changed` などの別名を出さない。
- 読み込み時に別名を勝手に正規化しない。

`DeviceDataType` は以下だけを許可する。

- `Bit`
- `Int16`
- `UInt16`
- `Int32`
- `UInt32`
- `Float32`

## QR生成手順

1. PC画面の入力内容からProject JSON objectを作る。
2. PC保存用にはpretty JSONを作る。
3. QR用にはminified JSON bytesを作る。
4. minified JSON bytesのSHA-256 hexを作る。
5. minified JSON bytesをraw deflateで圧縮する。
6. 圧縮bytesをbase64url without paddingにする。
7. `QR 1枚の文字数` で分割する。
8. 各QRを `PLCIOC2D|session|index|total|sha256|payload-chunk` で作る。
9. 画面にQR画像、ページ番号、総枚数、chunk size、形式を表示する。
10. PNG保存時も同じ文字列をQRに入れる。

## 検証

最低限の検証:

- .NETで生成したQR本文を復元して、元のminified JSON bytesと一致する。
- SHA-256が一致する。
- Android実機で読める。
- iOS実機で読める。
- 1枚QRだけでなく複数枚QRでも完了する。
- 読込完了後、スマホ側で案件名、PLC設定、デバイス、タイムチャート、トラップが反映される。

失敗時の表示:

- 「PLC IO Checker用ではない」と断定しない。
- QRが検出できても復元/チェックサムで失敗する場合がある。
- 「QRを検出しましたが、読み取りに失敗しました。PC側でQR 1枚の文字数を下げて再生成してください。」のように案内する。

## 実装で避けること

- 古いQR形式のfallback。
- 不正enum値の自動修正。
- Python `zlib.compress()` 相当のzlib container出力。
- 端末で読めないほど密なQRを初期値にすること。
- GUIで自由入力させてからスマホ側に救済させる設計。
- QR全枚数が揃っただけで成功扱いにすること。復元、SHA検証、JSON取り込み完了までを成功条件にする。

## 参考ファイル

- `qr_payload.py`: QR payload生成と復元の基準実装。
- `app.py`: Python試作GUI。
- `README.md`: 現行QR形式と圧縮注意点。
- `MEMO.md`: 方針メモ。
