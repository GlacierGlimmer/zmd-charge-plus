# Changelog / 更新日志

所有正式发布版本在此记录。版本号由项目维护者明确决定，不随源码整理自动变化。

## [Unreleased]

- GitHub 首次源码发布与 Windows / Microsoft Store 发布流程准备。

## [0.1.0] - 2026-09-23

首次正式版本的源码基线（以最终 GitHub Release 为准）。

- 可自定义 Windows HUD，支持方案、布局、动画与透明度。
- 系统、硬件、网络、进程、时间、DeepSeek API 与自定义 HTTP/JSON 数据源。
- 内置变量库分类整理，维护变量到运行时生产代码的静态覆盖检查。
- 简体中文 / English，本地语言偏好与首次启动系统语言判断。
- DeepSeek 峰谷判定以北京时间为准，同时提供用户本地时区的切换时间变量。
- 单实例、托盘操作、开机启动、配置管理和日志。
- GitHub Portable 构建脚本支持 win-x64 / win-x86 / win-arm64 的 self-contained 单文件 EXE，并生成 SHA-256 清单。

> 此条为源码版本记录，不表示三个架构均已经完成实机验收，也不表示 GitHub Release 已发布。
