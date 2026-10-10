# 版本约定 / Version policy

当前源码版本：`v0.1.1`。本次版本依据 2026-10-10 GitHub Releases 快照确定，尚未创建新 Release。

仅在用户明确要求时修改产品版本。修改时查询本平台独立仓库的全部已发布 Releases（含 beta／prerelease），按最大语义版本的 patch 加 1，去掉 beta 后缀。例如已有 v0.1.1 或 v0.1.1-beta，则设为 v0.1.2。构建、修复、CI、测试日期和打包都不自动递增版本。

同步更新应用版本、程序集／安装版本、当前版本文案与文档。历史发布记录、测试包和校验记录保留当时真实版本。下载链接使用本平台 Releases 页面，不指向尚未发布的安装包。未经用户明确要求不创建 Release 或发布标签。

Change the product version only on an explicit user request. At that time, inspect this platform repository's published Releases, including beta releases, increment the highest semantic patch version, and use a stable version without a beta suffix. Builds and CI never increment it automatically. Historical records retain their actual versions. Publishing requires a separate user request.
