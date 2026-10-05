export interface CpuMemSample {
  cpuPct: number;
  memUsedGb: number;
  memTotalGb: number;
  memPct: number;
}

export interface NetworkSample {
  downMBs: number;
  upMBs: number;
}

export interface GpuSample {
  gpuPct: number;
  gpuMemPct: number;
  gpuTemp: number;
}
