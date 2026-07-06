# Changelog

All notable changes to PLC IO Checker ProjectBuilder will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

**Entry labels**

- `Project JSON`: Project JSON、QR、schema、import/export。
- `App`: ProjectBuilder GUI/Core の動作。
- `Data`: 保存データ、表示型、コメント。
- `Docs`: 仕様書、README、GUI 要件などの文書。
- `Tests`: テスト、fixture、検証データ。
- `Tooling`: ビルド、CLI、開発補助。

## [Unreleased] - 2026-07-06

### BREAKING

- Project JSON: MELSEC 接続の `melsec.moduleIoNo`(16進文字列)を廃止し、正準13名の
  `melsec.moduleIo`(例 `"OwnStation"`)に変更しました。モバイルアプリ(Android/iOS)の
  同日変更と対で、旧形式の JSON は読み込みエラーになります。

### Changed

- App: ユニットI/O の入力を自由入力テキストから正準13択のプルダウンに変更しました。

## [Unreleased] - 2026-07-05

### Changed

- Project JSON: PLC 機種候補を Android/iOS の新しい通信プロファイルに合わせ、`melsec:qcpu` を廃止してユニット付き MELSEC / KEYENCE KV-NANO 候補を出力できるようにしました。

### Added

- Tests: ProjectBuilder の機種表示ラベルと `plc.cpuModel` の相互変換が、新しい候補一覧に一致することを確認する Core テストを更新しました。

## [Unreleased] - 2026-06-28

### Changed

- App: Clipboard import は明示されたデータ型と Trap 条件を必須にし、別名変換、データ型推測、Trap 条件の既定値補完で成功扱いにしないようにしました。
- Project JSON: JSON / QR 生成時はデータ型未指定をエラーにし、同一アドレスで既に明示済みのデータ型がある場合だけその型で補完するようにしました。
- Project JSON: 同一アドレスに異なるデータ型が明示されている場合、先勝ちで寄せず生成エラーにするようにしました。
- App: List の新規行と範囲追加でデータ型を `Bit` / `Int16` へ自動入力せず、未指定のまま扱うようにしました。
- App: Trap 条件がアドレス種別に合わない場合に Rise / GreaterOrEqual などへ自動置換せず、条件未指定として生成時にエラーにするようにしました。
- App: 数値入力が不正な場合に既定値へ戻さず、入力エラーとして扱うようにしました。
- Docs: Clipboard import の列構成と、別名・推測・暗黙既定値を受け付けない方針を README / GUI 要件へ追記しました。

### Fixed

- App: Excel とのコピー/貼り付け時にクリップボードが一時的に使用中でもアプリが落ちないよう、再試行とエラー表示を追加しました。
- App: Excel へコピーする TSV でタブ、改行、引用符を含むセルを正しくエスケープするようにしました。

### Added

- Tests: Clipboard import が別名、未指定データ型、不正 Trap 条件を受け付けないことを確認する WPF テストを追加・更新しました。
- Tests: JSON / QR 生成で、同一アドレスの明示済み型だけが未指定行へ補完され、明示型の衝突はエラーになることを確認する Core テストを追加しました。

## [Unreleased] - 2026-06-27

### Changed

- Project JSON: Project JSON を初回公開向けの schema v1 として整理しました。
- Project JSON: QR payload prefix を `PLCIOC1|ZSTD` に変更しました。
- Project JSON: Project JSON のコメントと表示型を、アドレス別の `deviceMeta` に一本化しました。
- Project JSON: JSON / QR 出力に出力元とバージョンを示す `exportInfo` を追加しました。
- Project JSON: `deviceList` / `timeChart` は `address` のみを持つ形にし、Trap 側も表示型を持たず `deviceMeta` から解決する形にしました。
- App: ProjectBuilder の JSON import は schema v1 を必須にし、`deviceMeta` から各行の表示型とコメントを復元するようにしました。
- App: UI 表記を Android/iOS 側に合わせ、`PLC settings`、`PLC IP / host`、`KEYENCE device mode`、`List`、`Trap`、`Comment`、`Time Chart` に整理しました。
- App: Comment タブで `deviceMeta` に保存するアドレスコメントとデータ型を編集できるようにしました。
- App: Excel 貼り付けの引用符付きセル、セル内改行、末尾空列、Trap の空データ型列を正しく処理するようにしました。
- App: 行削除はグリッド行を明確に選択しているときだけ Delete キーで実行されるようにし、コメント編集中の文字削除と衝突しないようにしました。
- Data: 同じアドレスのコメントと表示型が List / Time Chart / Trap / Comment でずれないよう、ProjectBuilder 内部でもアドレス単位で共通化しました。
- Data: List / Time Chart / Trap にコメント列を追加し、貼り付けたコメントを `deviceMeta` に反映するようにしました。
- Data: コメントを最大1024文字に制限し、GUI入力、貼り付け、Project JSON / QR 出力で同じ上限を適用するようにしました。
- Data: コメント保存時の改行を空白へ正規化し、コメントを1行コメントとして扱うようにしました。
- Data: スマホアプリで使えないデバイスは受け付けず、入力中に正規化とエラー表示を行うようにしました。
- Data: KEYENCE XYM の X/Y アドレス生成を Android/iOS と同じ `X0` から `XF`、`X10` の表記に揃えました。
- Docs: README、Build、GUI 要件、QR / Project JSON 仕様書を現在の schema v1 / `PLCIOC1` / List / Trap 表記に合わせました。

### Added

- Tests: `deviceMeta` 形式、schema v1、`PLCIOC1|ZSTD` の出力を確認する Core テストを追加・更新しました。
- Tests: モバイル対応デバイス、アドレス正規化、データ型共有、コメント1024文字制限、Trap 条件の Core テストを追加・更新しました。
- Tests: Excel 貼り付けパーサーとヘッダー判定の WPF テストを追加しました。
