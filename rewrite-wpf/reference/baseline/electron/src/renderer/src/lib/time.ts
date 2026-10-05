// 将本机时间格式化为固定的 24 小时制 HH:mm:ss。
export function formatLocalTime(date: Date): string {
  const pad = (value: number): string => String(value).padStart(2, '0');
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;
}
