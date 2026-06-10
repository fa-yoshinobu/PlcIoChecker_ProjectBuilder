# refactor-instructions.md

PLC IO Checker Project Builder のリファクタリング指示書。
この文書は実装担当モデル向けの完結した作業指示である。実装前にこの文書全体を読むこと。

> **実行環境の前提**: WPF(`net8.0-windows`)のビルドには **Windows + .NET 8 SDK** が必要。
> Windows 以外で起動された場合は、`PlcIoCheckerQr.Core` とそのテストのみ扱えることを報告し、
> WPF 側の変更は行わない。

---

## Objective

アプリ(Android / iOS)との共有契約(プロジェクト JSON schema v3、QR チャンク形式
`PLCIOC3|ZSTD|...` + Zstd 圧縮)を一切壊さずに:

1. **`ProjectFactory`(Core のドメインルール)に特性テストを追加する**(現在ほぼ未テスト)
2. **`MainWindow.xaml.cs`(2,600 行の god code-behind)から純粋ロジックを抽出してテスト可能にし、
   残りを partial class ファイルに分割する**

「MVVM への全面書き換え」「UI フレームワークの変更」「見た目の変更」は目的ではなく禁止事項である。

---

## Project Understanding

### 何のツールか

PLC IO Checker(Android / iOS アプリ)のプロジェクト設定を PC で作成し、JSON ファイルまたは
QR コード(複数ページ)でモバイルアプリへ転送する Windows WPF ツール(.NET 8、配布版 0.3.0)。
スマホで長い PLC 設定を入力する代わりに、PC で編集して QR で読み込ませる。
Excel からの行貼り付け(クリップボード)に対応する。

### 構成(C# 計約 4,240 行)

| プロジェクト | ファイル | 行数 | 内容 |
|---|---|---|---|
| `PlcIoCheckerQr.Core` | `ProjectModel.cs` | 713 | `PlcProject` 等のレコード型 + `ProjectFactory`(**ドメインルールの中心**: `GuessDataType` / `IsBitAddress` / `TrapConditionsForAddress` / `CoerceTrapConditionForAddress` / `MakeProject` / `Slugify` / 各種定数表) |
| | `ProjectQrPayload.cs` | 274 | JSON 出力形状(`ToJsonShape`)、QR チャンク encode/decode、Zstd(ZstdSharp.Port) |
| `PlcIoCheckerQr.Wpf` | `MainWindow.xaml.cs` | 2,600 | **god code-behind**(後述 D1) |
| | `MainWindow.xaml` | 623 | 画面定義 |
| | `Localization/LanguageCatalog.cs` | 135 | `Languages/*.json`(en / ja)の読込・整形 |
| | `Windows/AboutWindow.*` ほか | 小 | |
| `PlcIoCheckerQr.Core.Tests` | `ProjectQrPayloadTests.cs` | 395 | QR チャンク / JSON 形状のテスト(xUnit)。**Core 唯一のテストファイル** |

### `MainWindow.xaml.cs` の中身(行番号は調査時点 main, commit `dc0b08b`)

- 行ビュー行クラス: `DeviceRow` / `WatchRow` / `TrapRow`(ベンダ・KEYENCE モード別の
  データ型正規化を内包、32–247 行)
- Excel 貼り付け用の多言語インポートエイリアス照合(270–353 行)と
  クリップボード解析(`SplitClipboardLine` / `IsAddressClipboardHeader` /
  `IsTrapClipboardHeader` / `ParseClipboardBoolean`、1404–1697 行)
- DataGrid 列テンプレートのコード生成(411–543 行)
- プロジェクト構築 / QR 生成・表示・自動送りタイマ(655–846、1827–1965 行)
- プロジェクト JSON 読込(`LoadProjectJson` 2259 行〜)+ JSON 解析ヘルパ
  (`RequireProjectJsonV2` / `ReadRequired*` 2486–2600 行)
- UI 値マッピング(`ToUiVendor` / `ToUiDataType` 等の純粋 switch、2359–2408 行)
- 数値解析・クランプ(`ParseRange` / `ParseHexInt` / `Clamp` 等、2430–2484 行)
- ローカライズ適用(868–1018 行)、検証・ステータス表示、行移動・選択ユーティリティ

### 共有契約(他リポジトリとの結合点)

- **プロジェクト JSON**: `plc-io-checker-project` schema v3。Android
  (`data/ProjectJsonV2.kt`)/ iOS(`Services/ProjectJsonV2.swift`)が読む。
  仕様は `docs/QR_JSON_FORMAT.md`。ProjectBuilder は共有 v3 フィールドのみ出力し、
  UI 専用設定は出力しない。MELSEC ルーティングは 10 進 `networkNo` / `stationNo` と
  `0x` 付き 16 進文字列 `moduleIoNo` / `multidropNo`。KEYENCE は `remotePassword` を出さない。
- **QR チャンク形式**: `PLCIOC3|ZSTD|<session>|<index>|<total>|<checksum>|<payload>`。
  読取側は Android `model/ProjectQrPayload.kt` + ネイティブ Zstd、iOS `ProjectQrCollector` +
  `ZstdBridge`。`ProjectQrPayloadTests` が双方向の契約テスト。
- 名称メモ: クラス名 `RequireProjectJsonV2` / アプリ側 `ProjectJsonV2` は「v2 シリアライザ」
  という一族共通の歴史的名前で、出力する `schemaVersion` は 3。**名前を「直さない」こと。**

### CI / 配布

- `.github/workflows/dotnet-ci.yml`: restore → build(Release)→ test → single-file publish
  スモーク(exe と `Languages/en.json` / `ja.json` の存在検証)。windows-latest。**完備。**
- `release.yml`: GitHub Release への zip 配布。**触らない。**
- ビルド: `dotnet build dotnet/PlcIoCheckerQr.sln`、配布: `build-dotnet-onefile.bat`。

---

## Behaviors To Preserve(絶対に壊さない既存挙動)

1. **出力 JSON のバイト互換**: フィールド名・順序ポリシー・数値 / 16 進文字列の書式・
   省略規則(`ToJsonShape`、`PrettyJsonOptions` / `MinifiedJsonOptions`)。
   同一入力に対する出力 JSON が変更前後で一致すること。
2. **QR チャンク形式**: プレフィックス `PLCIOC3`、`ZSTD`、区切り、checksum、chunk 分割規則。
   `ProjectQrPayloadTests` は編集禁止。
3. **JSON 読込の受理範囲**: `LoadProjectJson` が現在受理する JSON は受理し続け、
   現在エラーにするものはエラーのままにする(寛容化・厳格化のどちらもしない)。
4. **ドメインルール**: `GuessDataType` / `IsBitAddress` / `TrapConditionsForAddress` /
   `Coerce` / `Validate` / 20 件上限(`MaxTimeChartTargets` / `MaxTrapDefinitions`)/
   CPU モデル一覧 / データ型一覧。これらはアプリ側の挙動と対応しており、
   ProjectBuilder 単独で「改善」してはならない。
5. **Excel 貼り付けの受理形式**: 列順(`Address / Data type / Comment`)、多言語ヘッダ
   エイリアス、トラップ条件エイリアス。
6. **UI の見た目・文言・操作**: 変更しない。`Languages/*.json` も変更しない。
7. **単一ファイル publish 構成**: csproj の publish 関連設定、`Languages/*.json` の
   コピー / 埋め込み二重化(CI が検証している)。
8. **バージョン**: `0.3.0` を変更しない。リリース作業をしない。

---

## Non-Negotiables(交渉不可の制約)

- 最初に `git status` を確認する。未コミット変更があれば混ぜず、報告して停止する。
- 編集前に Baseline Commands をすべて実行し、結果(テスト件数含む)を記録する。
- 変更は小さく戻しやすい単位。コミットはユーザーの指示があるまで行わない。
- NuGet 依存の追加・更新をしない(テストは既存の xUnit 構成で書く)。
- `MainWindow` は WPF の partial class のまま分割する(`MainWindow.xaml` との対応を壊さない)。
  XAML は変更しない。
- 抽出した純粋ロジックの可視性は `internal` まで。テストから参照する場合は
  `InternalsVisibleTo` を使う(public 化しない)。
- MVVM 化・ViewModel 導入・データバインディングの変更をしない(提案のみ可)。
- `docs/`、`Languages/*.json`、既存テストファイル、release.yml を変更しない
  (テストは新規ファイル追加のみ可)。
- 正しさが不明な場合は実装を止め、「Stop And Ask」として質問を報告書に書く。
- 各フェーズ完了ごとに Verification Requirements を実行する。

---

## Stop And Ask Conditions(即時停止して質問する条件)

- 特性テスト作成中に、`ProjectFactory` のルールがアプリ側
  (`../PlcIoChecker_Android/app/src/main/java/com/fa_labo/plc_io_checker/model/AppModels.kt` /
  `../PlcIoChecker_iOS/ios-app/PlcIoChecker/Models/AppModels.swift`)と食い違って見えた
  (**修正せず**、差異を質問として残す。どちらが正かはプロダクト判断)
- 出力 JSON / QR チャンクのバイト列に影響しうる変更が必要に見えた
- `LoadProjectJson` の受理範囲を変えないと抽出できない構造に見えた
- 既存テスト(`ProjectQrPayloadTests`)が自分の変更後に落ちた ⇒ 即座に巻き戻して報告
- 削除候補コードが本当に未使用と証明できない
- 本書の Debt Map に無い大きな問題を発見した(報告のみ)

---

## Baseline Commands

作業ディレクトリ: リポジトリルート。Windows + .NET 8 SDK。

```powershell
git status                                                  # クリーンであることを確認
dotnet restore dotnet/PlcIoCheckerQr.sln
dotnet build dotnet/PlcIoCheckerQr.sln --configuration Release --no-restore
dotnet test dotnet/PlcIoCheckerQr.sln --configuration Release --no-build --verbosity normal
```

最終確認用(CI と同じ publish スモーク。時間がかかるため baseline と最終のみ):

```powershell
dotnet publish dotnet/src/PlcIoCheckerQr.Wpf/PlcIoCheckerQr.Wpf.csproj `
  --configuration Release --runtime win-x64 --self-contained true `
  --output publish-smoke /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true `
  /p:DebugType=None /p:DebugSymbols=false
# publish-smoke/PlcIoCheckerProjectBuilder.exe と publish-smoke/Languages/{en,ja}.json の存在確認
# 確認後 publish-smoke/ は削除する(リポジトリに残さない)
```

---

## Debt Map

行番号は調査時点(main, commit `dc0b08b`)のアンカー。ドリフトしていたら宣言名で探すこと。

### D1. `MainWindow.xaml.cs` が 2,600 行の god code-behind 【実装可 / 主作業】

- **根拠**: Project Understanding に列挙したとおり、行クラス・クリップボード解析・
  JSON 読込・QR 生成・ローカライズ・汎用ユーティリティが 1 ファイルに同居。
- **なぜ負債か**: 純粋ロジック(貼り付け解析、JSON 解析、UI 値マッピング、数値解析)が
  UI イベントと同居しているためテスト不能。修正のたびに 2,600 行を読む必要がある。
- **影響範囲**: WPF アプリ全体。**変更リスク**: 手順を守れば低〜中。
- **改善案**(2 段階):
  1. **純粋 static ヘルパの抽出**: 以下を WPF プロジェクト内の `internal static` クラス群
     (例: `ClipboardImport.cs`、`ProjectJsonReader.cs`、`UiValueMapping.cs`、
     `NumericParsing.cs`)へ move-only で移す。対象はすべて `static` または容易に static 化
     できる(インスタンス状態は言語カタログ程度。言語依存のものは引数で渡す):
     - クリップボード: `SplitClipboardLine` / `IsAddressClipboardHeader` /
       `IsTrapClipboardHeader` / `ParseClipboardBoolean` / `DeviceCommentFromFields` /
       エイリアス照合(`MatchesImportAlias` 系、`LoadImportAliases`)
     - JSON 読込: `RequireProjectJsonV2` / `ReadRequiredObject` / `ReadRequiredArray` /
       `ReadOptionalString` / `ReadRequiredString` / `ReadRequiredInt` / `ReadRequiredHexInt`
     - UI 値マッピング: `ToUiVendor` / `ToUiConnectionMode` / `ToUiKeyenceMode` /
       `ToUiTransport` / `ToUiDataType` / `ToUiTrapCondition` / `DisplayVendor` /
       `TrapConditionDisplayText` / `FormatPrefixedHex`
     - 数値解析: `ParseInt` / `ParseDouble` / `ParseHexInt` / `Clamp` / `FormatSeconds`
  2. **partial class 分割**: 残った code-behind を partial ファイルへ move-only で分割
     (例: `MainWindow.Rows.cs`(行クラス)、`MainWindow.Grids.cs`(列テンプレート)、
     `MainWindow.Qr.cs`(QR 表示・自動送り)、`MainWindow.JsonIo.cs`(Load/Save)、
     `MainWindow.Clipboard.cs`、`MainWindow.Localization.cs`)。クラス名・メンバ名は変えない。
- **検証**: ビルド + 全テスト + アプリ起動スモーク(後述)。抽出した純粋ヘルパには
  Phase 1 / 3 で特性テストを付ける。

### D2. `ProjectFactory`(ドメインルール)が未テスト 【実装可 / 先にやる】

- **根拠**: Core のテストは `ProjectQrPayloadTests.cs` のみ。`GuessDataType` /
  `IsBitAddress` / `TrapConditionsForAddress` / `CoerceTrapConditionForAddress` /
  `ValidateTrapConditionForAddress` / `TrapConditionRequiresThreshold` / `MakeProject`
  (20 件上限の切り詰め含む)/ `Slugify` / `SupportedDeviceNames` に直接テストが無い。
- **なぜ負債か**: これらはアプリ側のデバイス種別・トラップ規則のミラーであり、
  契約級のロジックなのに回帰検出手段が無い。
- **改善案**: 新規テストファイル(例: `ProjectFactoryTests.cs`)で特性テストを追加。
  期待値は**現在の実装出力**(MELSEC / KEYENCE Normal / KEYENCE Xym の各代表アドレスと
  境界値)。アプリ側との食い違いを見つけたら Stop And Ask(修正しない)。
- **検証**: `dotnet test` 通過。追加テスト一覧を報告書に記載。

### D3. その他(現状維持 / 報告のみ)

- `ProjectQrPayload.cs` / `ProjectModel.cs` のレコード型: 健全。触らない
  (D2 のテスト追加のみ)。
- `LanguageCatalog.cs`: 小さく明確。触らない。
- CI / release ワークフロー: 完備。触らない。`dotnet format` ゲートの追加は
  差分ノイズが大きいため**提案のみ**。
- `RequireProjectJsonV2` という名前と `schemaVersion: 3` の不一致は一族共通の歴史的命名。
  rename しない(Behaviors To Preserve 参照)。
- `artifacts/` / `dotnet/publish/` は Git 管理外のビルド出力。触らない。

---

## Implementation Phases

各フェーズの最後に必ず Verification Requirements を実行し、通ってから次へ進む。

### Phase 0: 現状確認

1. Windows + .NET 8 SDK であることを確認(違えば停止・報告)
2. `git status` 確認(クリーンでなければ停止・報告)
3. Baseline Commands を実行し、結果を記録(publish スモーク含む)

### Phase 1: 安全網(特性テスト)の追加 — Core(D2)

1. `ProjectFactoryTests.cs` を新規追加し、ドメインルールの現挙動を固定する
2. 期待値は現在の実装出力。仕様の発明禁止。アプリ側との食い違いは Stop And Ask に記録

### Phase 2: `MainWindow.xaml.cs` からの純粋ヘルパ抽出(D1-1)

1. 1 グループ(クリップボード → JSON 読込 → UI マッピング → 数値解析)ずつ
   `internal static` クラスへ move-only 移動し、都度ビルド + テスト
2. 言語カタログ等のインスタンス依存があれば引数化はせず、その関数はスキップして報告

### Phase 3: 抽出ヘルパへの特性テスト追加

1. WPF プロジェクト参照のテストは追加できない(`net8.0-windows`)ため、次のいずれかを選ぶ:
   - (a) 抽出先を Core プロジェクトに置けるもの(WPF 型に依存しないもの)は Core へ置き、
     既存テストプロジェクトでテストする
   - (b) WPF 型依存が残るものは WPF プロジェクト内に置き、テスト対象外として報告する
2. どちらを選んだかと理由を報告書に記載(`InternalsVisibleTo` が必要なら Core 側のみに追加)

### Phase 4: partial class 分割(D1-2)

1. 1 ファイルずつ partial へ移動し、都度ビルド
2. メンバ名・アクセス修飾子・ロジックは一切変えない

### Phase 5: 検証と報告

1. 全 Verification Requirements(publish スモーク + 起動スモーク含む)を最終実行
2. Reporting Format に従って報告書を作成

### Phase 6(提案のみ・実装禁止)

- MVVM / ViewModel 化の段階的移行案
- `dotnet format` の CI ゲート追加
- `LoadProjectJson` の解析部を Core へ移して JSON 読込もクロスプラットフォームでテストする案

---

## Verification Requirements

各フェーズ完了時に最低限:

```powershell
dotnet build dotnet/PlcIoCheckerQr.sln --configuration Release
dotnet test dotnet/PlcIoCheckerQr.sln --configuration Release --no-build
```

最終フェーズでは追加で:

1. publish スモーク(Baseline Commands 参照)
2. **アプリ起動スモーク**: ビルドした exe を起動し、以下を目視確認
   - 起動してメイン画面が表示される(en / ja 切替)
   - デバイス行を 1 件追加 → `Generate` → QR が表示される
   - `Save JSON` で出力した JSON が、変更前のビルドで出力した同条件の JSON と
     **テキスト一致**する(これが最重要の互換確認)
   - `Load JSON` で出力した JSON を読み戻せる
3. baseline で通っていたテストがすべて通り、件数が増えていること(D2 / Phase 3 の追加分)
4. 既存テストファイルと `docs/` / `Languages/` が無変更であること(`git diff --stat` で確認)

---

## Reporting Format

作業完了時(または中断時)に以下を Markdown で報告する:

1. **実行環境**: Windows / .NET SDK のバージョン
2. **Baseline 結果**: 実行コマンドと結果(テスト件数)
3. **Phase 1 / 3 の追加テスト**: テスト名と固定した挙動の要約
4. **アプリ側との食い違い**: 見つけた場合はルール名・ProjectBuilder の挙動・アプリの挙動を併記(修正はしない)
5. **Phase 2 / 4 の移動一覧**: 移動した宣言と移動先ファイル、スキップした宣言とその理由
6. **JSON 互換確認の結果**: 変更前後の出力 JSON 一致確認の方法と結果
7. **各フェーズの検証結果**: 最後に実行したコマンドと結果(失敗を隠さない)
8. **Stop And Ask**: 発生した質問と停止範囲
9. **Phase 6 提案**: 実装しなかった設計案
10. **未実施事項**: 起動スモークができなかった等の明記

---

## Out-of-scope Items(やらないこと)

- 出力 JSON・QR チャンク形式・Zstd 圧縮パラメータの変更
- `LoadProjectJson` の受理範囲の変更(寛容化・厳格化とも)
- ドメインルール(`ProjectFactory`)の挙動変更(アプリとの食い違いを見つけても報告のみ)
- MVVM 化・XAML 変更・UI / 文言 / `Languages/*.json` の変更
- NuGet 依存の追加・更新(QRCoder / ZstdSharp.Port のバージョン変更を含む)
- バージョン番号変更、リリース作業、`release.yml` / CI の変更
- `RequireProjectJsonV2` 等の歴史的命名の rename
- `docs/`・既存テストファイルの変更
- 他リポジトリ(`PlcIoChecker_Android` / `PlcIoChecker_iOS` ほか)の変更(参照のみ)
- 「死コード」と思われるものの削除(参照ゼロを証明できない限り)
