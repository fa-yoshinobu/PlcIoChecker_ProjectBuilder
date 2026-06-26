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

## [Unreleased] - 2026-06-26

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
- Data: スマホアプリで使えないデバイスは受け付けず、入力中に正規化とエラー表示を行うようにしました。
- Docs: README、Build、GUI 要件、QR / Project JSON 仕様書を現在の schema v1 / `PLCIOC1` / List / Trap 表記に合わせました。

### Added

- Tests: `deviceMeta` 形式、schema v1、`PLCIOC1|ZSTD` の出力を確認する Core テストを追加・更新しました。
- Tests: モバイル対応デバイス、アドレス正規化、データ型共有、Trap 条件の Core テストを追加・更新しました。
- Tests: Excel 貼り付けパーサーとヘッダー判定の WPF テストを追加しました。
