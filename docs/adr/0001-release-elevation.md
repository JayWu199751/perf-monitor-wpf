# 发布版采用可继续普通运行的一次提权路径

Status: accepted

Release 以 asInvoker 启动；每次尚未提权的 Release 启动最多自动尝试一次 runas，同一次启动中不重试。用户拒绝或提权启动失败时，当前实例继续以普通权限运行，温度读数显示为缺失。该选择保留旧版启动时请求管理员权限的体验，同时避免 requireAdministrator manifest 在拒绝 UAC 时无法启动当前实例；跨权限实例必须交接为单实例。
