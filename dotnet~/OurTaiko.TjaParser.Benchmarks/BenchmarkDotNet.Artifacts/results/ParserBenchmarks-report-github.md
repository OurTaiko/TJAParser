```

BenchmarkDotNet v0.15.2, Linux Ubuntu 24.04.5 LTS (Noble Numbat)
Intel Xeon Processor 2.10GHz, 1 CPU, 4 logical and 4 physical cores
.NET SDK 10.0.114
  [Host]     : .NET 10.0.14 (10.0.1426.52326), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-YFEFPZ : .NET 10.0.14 (10.0.1426.52326), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

IterationCount=10  WarmupCount=3  

```
| Method       | Chart       | Mean        | Error        | StdDev      | Gen0      | Gen1      | Gen2      | Allocated   |
|------------- |------------ |------------:|-------------:|------------:|----------:|----------:|----------:|------------:|
| **SongInfoRead** | **Donkama2000** |    **249.2 μs** |     **18.34 μs** |    **10.91 μs** |    **2.9297** |    **1.2207** |         **-** |   **400.62 KB** |
| Parse        | Donkama2000 |    343.8 μs |     53.24 μs |    31.68 μs |    3.4180 |    0.9766 |         - |   494.34 KB |
| OfFile       | Donkama2000 |  3,135.1 μs |    323.41 μs |   213.91 μs |   31.2500 |   19.5313 |         - |   4467.6 KB |
| **SongInfoRead** | **Large**       |  **1,596.7 μs** |    **171.62 μs** |   **113.51 μs** |  **142.5781** |  **142.5781** |  **142.5781** |   **1877.4 KB** |
| Parse        | Large       |  3,843.4 μs |    308.27 μs |   203.90 μs |   39.0625 |   39.0625 |   39.0625 |  3715.13 KB |
| OfFile       | Large       | 64,091.2 μs | 11,667.79 μs | 7,717.52 μs | 2333.3333 | 2333.3333 | 2333.3333 | 49477.16 KB |
