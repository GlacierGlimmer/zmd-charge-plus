# 第一次上传到 GitHub（GitHub Desktop）

目标仓库：`https://github.com/GlacierGlimmer/zmd-charge-plus`

这个压缩包是**源码仓库内容**，不是供普通用户直接安装的 Release 文件，也不是应该直接上传至 GitHub Releases 的附件。

1. 确认 GitHub Desktop 已克隆空仓库，路径示例：`D:\GitHub\zmd-charge-plus`。
2. 解压源码包。打开压缩包内的 `zmd-charge-plus` 文件夹，**只复制其中的所有文件与子文件夹**（包括 `.gitignore`、`.gitattributes`、`.github`）到上面的已克隆仓库目录。
3. 如果 Windows 问是否合并目录，选择合并；**不要删除仓库里原有的隐藏 `.git` 目录**。不要把整个外层 `zmd-charge-plus` 再套一层。
4. 回到 GitHub Desktop，左侧 Changes 应出现源码文件。确认没有 `bin/`、`obj/`、`dist/`、`publish/`、`settings.json`、API Key 或证书文件。
5. Summary 填 `Initial source import: Endfield Charge Plus v0.1.0`，点击 **Commit to main**，然后点击 **Push origin**。
6. 刷新仓库网页，应看到 `README.md`、`EndfieldChargePlus.sln`、`Customization/`、`Settings/` 等；Actions 页面会显示初次 Windows 构建检查结果。

**此时先不要新建 v0.1.0 tag / Release**：先完成源码仓库首次推送和 Actions / x64 测试，再单独上传最终的三个 Portable EXE 与 SHA-256 文件。

注意：源码包内的 `.github/workflows/build.yml` 只检查代码，不自动发布可执行文件，也不进行代码签名。
