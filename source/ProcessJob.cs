using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AisoundStudio;
/// <summary>A Windows job owns every child process and kills them on close.</summary>
sealed class ProcessJob : IDisposable
{
    IntPtr handle;
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits
    {
        public long PerProcessTime,PerJobTime;
        public uint LimitFlags;
        public UIntPtr MinWorkingSet,MaxWorkingSet;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass,SchedulingClass;
    }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters
    {
        public ulong ReadOperations,WriteOperations,OtherOperations,ReadBytes,WriteBytes,OtherBytes;
    }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits
    {
        public BasicLimits Basic;
        public IoCounters Io;
        public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemory,PeakJobMemory;
    }
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string? name);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int infoClass,IntPtr info,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
    [DllImport("kernel32.dll",SetLastError=true)] static extern bool TerminateJobObject(IntPtr job,uint code);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    public ProcessJob(Process process)
    {
        handle=CreateJobObject(IntPtr.Zero,null);
        if(handle==IntPtr.Zero) throw new Win32Exception();
        try
        {
            var limit=new ExtendedLimits { Basic=new BasicLimits { LimitFlags=0x2000 } };
            var size=Marshal.SizeOf<ExtendedLimits>(); var memory=Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(limit,memory,false);
                if(!SetInformationJobObject(handle,9,memory,(uint)size)) throw new Win32Exception();
            }
            finally { Marshal.FreeHGlobal(memory); }
            if(!AssignProcessToJobObject(handle,process.Handle)) throw new Win32Exception();
        }
        catch { CloseHandle(handle); handle=IntPtr.Zero; throw; }
    }
    public void Stop() { if(handle!=IntPtr.Zero && !TerminateJobObject(handle,1)) throw new Win32Exception(); }
    public void Dispose() { if(handle!=IntPtr.Zero) { CloseHandle(handle); handle=IntPtr.Zero; } }
}
