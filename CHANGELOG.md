# Changelog

All notable changes to PLC IO Checker ProjectBuilder will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

**Entry labels**

- `Project JSON`: Project JSON、QR、schema、import/export。
- `App`: ProjectBuilder GUI/Core の動作。
- `Data`: 保存データ、表示型、コメント。
- `Docs`: 仕様書、README、GUI 要件、開発履歴などの文書。
- `Tests`: テスト、fixture、検証データ。
- `Tooling`: ビルド、CLI、開発補助。

## [Unreleased] - 2026-06-26

### Changed

- Project JSON: Project JSON を初回公開向けの schema v1 として整理しました。
- Project JSON: QR payload prefix を `PLCIOC1|ZSTD` に変更しました。
- Project JSON: Project JSON のコメントと表示型を、アドレス別の `deviceMeta` に一本化しました。
- Project JSON: `deviceList` / `timeChart` は `address` のみを持つ形にし、Trap 側も表示型を持たず `deviceMeta` から解決する形にしました。
- App: ProjectBuilder の JSON import は schema v1 を必須にし、`deviceMeta` から各行の表示型とコメントを復元するようにしました。
- Data: 同じアドレスの表示型が List / Time Chart / Trap でずれないよう、ProjectBuilder 内部でもアドレス単位で共通化しました。
- Docs: QR / Project JSON 仕様書、GUI 要件、開発履歴の記述を schema v1 / `PLCIOC1` に合わせました。

### Added

- Tests: `deviceMeta` 形式、schema v1、`PLCIOC1|ZSTD` の出力を確認する Core テストを追加・更新しました。

### Notes

- Tooling: WPF 本体は WindowsDesktop SDK が必要なため、この Mac では全体ビルド未実行です。
- Tests: Core テストは `DOTNET_ROLL_FORWARD=Major dotnet test dotnet/tests/PlcIoCheckerQr.Core.Tests/PlcIoCheckerQr.Core.Tests.csproj` で確認しています。
