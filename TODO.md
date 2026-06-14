# TODO

PLC IO Checker Project Builder の残りの公開作業と保守作業を管理する。

## 公開後 / 公開前の確認

- [x] Windows 版リリースにコード署名を付けるか決める。
  正式な Windows コード署名は有料証明書が必要になるため採用しない。
  GitHub Release パッケージは、インストーラーなし / コード署名なしの自己完結型 single-file EXE として公開する。
- [ ] 各リリース後に GitHub Release の配布ファイルを確認する。
  `PlcIoCheckerProjectBuilder-win-x64.zip` に `PlcIoCheckerProjectBuilder.exe` が含まれ、クリーンな Windows PC で起動することを確認する。
- [ ] 各リリース前に公開マニュアルへのリンクを確認する。
  アプリの Help メニューは `https://fa-yoshinobu.github.io/PlcIoChecker_Site/` を開き、上部ヘッダーのリンクは `https://fa-yoshinobu.github.io/PlcIoChecker_Site/projectbuilder/projectbuilder.html` を開く。
- [ ] ZIP 配布のみを継続するか、インストーラーを追加するか決める。
  現在の配布方法は GitHub Releases からの ZIP のみ。

## 保守メモ

- [x] ビルド出力は Git 管理外にしている。
  `dotnet/publish/` と `artifacts/` は意図的に Git から除外する。
- [x] ProjectBuilder マニュアルへのリンクはアプリ UI に配置済み。
  Help メニューは全体マニュアルサイトを開き、ヘッダーリンクは ProjectBuilder マニュアルページを開く。
- [x] リリースビルドの出力先は文書化済み。
  `build-dotnet-onefile.bat` で `dotnet/publish/win-x64/PlcIoCheckerProjectBuilder.exe` を生成する。
