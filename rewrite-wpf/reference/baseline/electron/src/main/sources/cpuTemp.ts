import { disposeWmi, readCpuTemperature } from '../wmi';

// CPU 温度直接查 WMI MSAcpi_ThermalZoneTemperature 的 CurrentTemperature，
// 不再经 systeminformation 启动常驻 PowerShell。需要管理员权限：非管理员下查询被拒
// 或机器没有 ACPI 热区时返回 null，界面显示 `--`，其余指标不受影响。
export function readCpuTemp(): number | null {
  try {
    return readCpuTemperature();
  } catch {
    return null;
  }
}

export function disposeCpuTemp(): void {
  disposeWmi();
}
