using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace Ets2Trainer
{
    // All addresses are module-relative. Never reuse a heap address across ticks.
    public static class Build162
    {
        public const string Version = "1.62.1.0";
        public const string Sha256 = "A938CB388121F9B884A2E1B0F048C230A7BC19638EF6D641A70775F8C7FEA956";
        public const long AppRoot = 0x3714f70, WorldRoot = 0x3714f30, EconomyRoot = 0x30aa8a0;
        public const long AppType = 0x2291ba8, WorldType = 0x22a1968, EconomyType = 0x229d3e8;
        public const long TruckType = 0x22bc218, ActorType = 0x23322c8;
        public const long VehicleType = 0x2310238, DriverType = 0x22d4048;
        public static bool IsSupportedHash(string value)
        {
            return String.Equals(value, Sha256, StringComparison.OrdinalIgnoreCase);
        }
    }

    public interface IMemory : IDisposable
    {
        long BaseAddress { get; }
        bool IsAlive { get; }
        byte[] Read(long address, int length);
        void Write(long address, byte[] bytes);
    }

    internal sealed class ProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        internal ProcessHandle() : base(true) { }
        protected override bool ReleaseHandle() { return Native.CloseHandle(handle); }
    }

    internal static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern ProcessHandle OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool ReadProcessMemory(ProcessHandle process, IntPtr address, byte[] buffer, UIntPtr count, out UIntPtr read);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool WriteProcessMemory(ProcessHandle process, IntPtr address, byte[] buffer, UIntPtr count, out UIntPtr written);
        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern UIntPtr VirtualQueryEx(ProcessHandle process, IntPtr address, out MemoryRegion region, UIntPtr size);
        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryRegion
        {
            internal IntPtr BaseAddress, AllocationBase;
            internal uint AllocationProtect;
            internal UIntPtr RegionSize;
            internal uint State, Protect, Type;
        }
    }

    public sealed class ProcessMemory : IMemory
    {
        private readonly Process process;
        private readonly ProcessHandle handle;
        public long BaseAddress { get; private set; }
        public int ProcessId { get { return process.Id; } }
        public string ExecutablePath { get; private set; }
        public bool IsAlive
        {
            get { try { return !handle.IsClosed && !process.HasExited; } catch { return false; } }
        }

        private ProcessMemory(Process target)
        {
            process = target;
            ExecutablePath = process.MainModule.FileName;
            BaseAddress = process.MainModule.BaseAddress.ToInt64();
            using (var file = File.Open(ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var sha = SHA256.Create())
            {
                string hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                if (!Build162.IsSupportedHash(hash))
                    throw new NotSupportedException("游戏文件不匹配。当前仅支持已验证的 ETS2 " + Build162.Version + " x64。检测到：" + FileVersionInfo.GetVersionInfo(ExecutablePath).ProductVersion);
            }
            // Query + read + write + VM operation; no debug privilege, driver or injection.
            handle = Native.OpenProcess(0x438, false, process.Id);
            if (handle == null || handle.IsInvalid)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法访问游戏，请以与游戏相同的权限运行修改器。");
            try
            {
                CheckBytes(0x3fc3b6, "488B1D738B3103");
                CheckBytes(0x5bf1bc, "F30F1180C0010000");
                CheckBytes(0x5bf2ae, "895150488B48188B5150");
                CheckBytes(0x4e2070, "488B05F92E230383B8C00A000000");
                CheckBytes(0x1b20e0b, "8981080100008B420489810C0100008B4208898110010000");
                CheckBytes(0x8536f0,"488B1E488B0B488D5330488B01FF9040020000");
            }
            catch { handle.Dispose(); throw; }
        }

        public static ProcessMemory Attach()
        {
            Process[] found = Process.GetProcessesByName("eurotrucks2");
            if (found.Length != 1)
            {
                foreach (var item in found) item.Dispose();
                throw new InvalidOperationException(found.Length == 0 ? "等待游戏启动" : "检测到多个游戏进程，请只保留一个。");
            }
            try { return new ProcessMemory(found[0]); }
            catch { found[0].Dispose(); throw; }
        }

        private void CheckBytes(long rva, string hex)
        {
            byte[] bytes = Read(BaseAddress + rva, hex.Length / 2);
            for (int i = 0; i < bytes.Length; i++)
                if (bytes[i] != Convert.ToByte(hex.Substring(i * 2, 2), 16))
                    throw new NotSupportedException("游戏内存代码校验失败，已禁止修改。");
        }

        public byte[] Read(long address, int length)
        {
            if (!IsAlive || !GameReader.IsPointer(address) || length <= 0)
                throw new IOException("游戏已退出或地址失效。");
            byte[] result = new byte[length]; UIntPtr count;
            if (!Native.ReadProcessMemory(handle, new IntPtr(address), result, (UIntPtr)length, out count) || count.ToUInt64() != (ulong)length)
                throw new IOException("读取游戏数据失败（" + Marshal.GetLastWin32Error() + "）。");
            return result;
        }

        public void Write(long address, byte[] bytes)
        {
            if ((bytes.Length != 4 && bytes.Length != 12 && bytes.Length != 16 && bytes.Length != 24 && bytes.Length != 32) || (address & 3) != 0 || !IsAlive || !GameReader.IsPointer(address))
                throw new IOException("拒绝向失效或未对齐的地址写入。");
            Native.MemoryRegion region;
            UIntPtr size = Native.VirtualQueryEx(handle, new IntPtr(address), out region, (UIntPtr)Marshal.SizeOf(typeof(Native.MemoryRegion)));
            uint protection = region.Protect & 0xff;
            if (size == UIntPtr.Zero || region.State != 0x1000 || (region.Protect & 0x100) != 0 ||
                (protection != 4 && protection != 8) || address + bytes.Length > region.BaseAddress.ToInt64() + (long)region.RegionSize.ToUInt64())
                throw new IOException("目标不是有效的可写数据页，已停止修改。");
            UIntPtr count;
            if (!Native.WriteProcessMemory(handle, new IntPtr(address), bytes, (UIntPtr)bytes.Length, out count) || count.ToUInt64() != (ulong)bytes.Length)
                throw new IOException("写入游戏数据失败（" + Marshal.GetLastWin32Error() + "）。");
        }
        public void Dispose() { if (handle != null) handle.Dispose(); process.Dispose(); }
    }

    public sealed class GameSnapshot
    {
        public bool Running, Driving;
        public long Application, World, Economy, Truck, Actor, Vehicle, Driver;
        public long FuelAddress, FatigueAddress;
        public float? Fuel;
        public uint? Fatigue;
        public uint GameMinutes, PauseCount, AppState;
        public string State = "等待游戏启动", FuelIssue = "", FatigueIssue = "";
        public bool CanFuel { get { return Driving && FuelAddress != 0 && Fuel.HasValue; } }
        public bool CanFatigue { get { return Driving && FatigueAddress != 0 && Fatigue.HasValue; } }
    }

    public static class GameReader
    {
        public static bool IsPointer(long value) { return value >= 0x10000 && value <= 0x00007ffffffeffff; }
        private static long Pointer(IMemory memory, long address)
        {
            long value = BitConverter.ToInt64(memory.Read(address, 8), 0);
            if (!IsPointer(value) || (value & 7) != 0) throw new IOException("等待游戏对象加载");
            return value;
        }
        private static uint UInt(IMemory memory, long address) { return BitConverter.ToUInt32(memory.Read(address, 4), 0); }
        private static long ObjectAt(IMemory memory, long address, long type)
        {
            long value = Pointer(memory, address);
            long actual = BitConverter.ToInt64(memory.Read(value, 8), 0);
            if (actual != memory.BaseAddress + type) throw new IOException("游戏对象类型不匹配，等待重新定位");
            return value;
        }
        public static GameSnapshot Read(IMemory memory)
        {
            var s = new GameSnapshot();
            s.Running = memory != null && memory.IsAlive;
            if (!s.Running) return s;
            try
            {
                s.Application = ObjectAt(memory, memory.BaseAddress + Build162.AppRoot, Build162.AppType);
                s.AppState = UInt(memory, s.Application + 0x594);
                s.PauseCount = UInt(memory, s.Application + 0xac0);
                s.Driving = s.AppState == 2 && s.PauseCount == 0;
                s.State = s.AppState != 2 ? "菜单中 / 等待读档" : s.PauseCount != 0 ? "游戏暂停 / 菜单中" : "驾驶场景已连接";
                if (s.AppState != 2) return s;
            }
            catch (IOException ex) { s.State = ex.Message; return s; }
            try
            {
                s.World = ObjectAt(memory, memory.BaseAddress + Build162.WorldRoot, Build162.WorldType);
                if ((UInt(memory, s.World + 8) & 0x80000000) == 0) throw new IOException("车辆场景已失效");
                s.Truck = ObjectAt(memory, s.World + 0x31b0, Build162.TruckType);
                s.Actor = ObjectAt(memory, s.Truck + 0x18, Build162.ActorType);
                s.Vehicle = ObjectAt(memory, s.Actor + 0x1f8, Build162.VehicleType);
                float fuel = BitConverter.ToSingle(memory.Read(s.Vehicle + 0x1c0, 4), 0);
                if (Single.IsNaN(fuel) || Single.IsInfinity(fuel) || fuel < 0 || fuel > 1.00001f)
                    throw new IOException("油量数据异常，已暂停燃油修改");
                s.Fuel = fuel; s.FuelAddress = s.Vehicle + 0x1c0;
            }
            catch (IOException ex) { s.FuelIssue = ex.Message; }
            try
            {
                s.Economy = ObjectAt(memory, memory.BaseAddress + Build162.EconomyRoot, Build162.EconomyType);
                if ((UInt(memory, s.Economy + 8) & 0x80000000) == 0) throw new IOException("驾驶员场景已失效");
                s.Driver = ObjectAt(memory, s.Economy + 0x18, Build162.DriverType);
                uint fatigue = UInt(memory, s.Driver + 0x50);
                if (fatigue > 10080) throw new IOException("疲劳数据异常，已暂停疲劳修改");
                s.Fatigue = fatigue; s.FatigueAddress = s.Driver + 0x50;
                s.GameMinutes = UInt(memory, s.Economy + 0x19c);
            }
            catch (IOException ex) { s.FatigueIssue = ex.Message; }
            return s;
        }
    }

    // The caller serializes Tick and switch changes. The GUI uses one shared lock.
    public sealed class TrainerEngine : IDisposable
    {
        private readonly IMemory memory;
        public bool FuelRequested { get; private set; }
        public bool FatigueRequested { get; private set; }
        public bool FuelActive { get; private set; }
        public bool FatigueActive { get; private set; }
        public long FuelWrites { get; private set; }
        public long FatigueWrites { get; private set; }
        public StabilityMode StabilityRequested { get; private set; }
        public bool StabilityActive { get; private set; }
        public long StabilityCorrections { get; private set; }
        public long RightingCount { get; private set; }
        private DateTime nextRighting;
        public StabilitySnapshot Stability { get; private set; }
        public GameSnapshot Snapshot { get; private set; }
        public string Error { get; private set; }
        private bool disposed;
        public TrainerEngine(IMemory target) { memory = target; Snapshot = new GameSnapshot(); Stability = new StabilitySnapshot(); Error = ""; }
        public void SetFuel(bool enabled) { FuelRequested = enabled && !disposed; FuelActive = false; }
        public void SetFatigue(bool enabled) { FatigueRequested = enabled && !disposed; FatigueActive = false; }
        public void SetStability(StabilityMode mode)
        {
            if((mode & ~(StabilityMode.Natural|StabilityMode.Strong))!=0)throw new ArgumentOutOfRangeException("mode");
            StabilityRequested=disposed?StabilityMode.Off:mode;StabilityActive=false;
        }
        public void DisableAll() { FuelRequested = FatigueRequested = FuelActive = FatigueActive = StabilityActive = false; StabilityRequested=StabilityMode.Off; }

        public void Tick()
        {
            FuelActive = FatigueActive = false;
            StabilityActive=false;
            if (disposed) return;
            if (!memory.IsAlive) { DisableAll(); Snapshot = new GameSnapshot(); return; }
            try
            {
                Error = ""; Snapshot = GameReader.Read(memory);
                if (FuelRequested && Snapshot.CanFuel)
                {
                    var check = GameReader.Read(memory);
                    if (SameFuelTarget(Snapshot, check))
                    {
                        memory.Write(check.FuelAddress, BitConverter.GetBytes(1.0f));
                        FuelWrites++; FuelActive = true;
                    }
                }
                if (FatigueRequested && Snapshot.CanFatigue)
                {
                    var check = GameReader.Read(memory);
                    if (SameFatigueTarget(Snapshot, check))
                    {
                        memory.Write(check.FatigueAddress, BitConverter.GetBytes((uint)0));
                        FatigueWrites++; FatigueActive = true;
                    }
                }
                if (FuelActive || FatigueActive) Snapshot = GameReader.Read(memory);
                Stability=new StabilitySnapshot();
                if(StabilityRequested!=StabilityMode.Off)
                {
                    Stability=StabilityReader.Read(memory,Snapshot);
                    if(Stability.Ready)
                    {
                        bool righted;
                        StabilityCorrections+=StabilityReader.Apply(memory,Snapshot,Stability,StabilityRequested,DateTime.UtcNow>=nextRighting,out righted);
                        if(righted){RightingCount++;nextRighting=DateTime.UtcNow.AddSeconds(2);}
                        StabilityActive=true;
                    }
                }
            }
            catch (IOException ex) { DisableAll(); Error = ex.Message; }
            catch (ObjectDisposedException) { DisableAll(); Snapshot = new GameSnapshot(); }
        }

        private static bool SameFuelTarget(GameSnapshot a, GameSnapshot b)
        {
            return b.CanFuel && a.Application == b.Application && a.World == b.World &&
                a.Truck == b.Truck && a.Actor == b.Actor && a.Vehicle == b.Vehicle && a.FuelAddress == b.FuelAddress;
        }
        private static bool SameFatigueTarget(GameSnapshot a, GameSnapshot b)
        {
            return b.CanFatigue && a.Application == b.Application && a.Economy == b.Economy &&
                a.Driver == b.Driver && a.FatigueAddress == b.FatigueAddress;
        }
        public void Dispose() { if (disposed) return; DisableAll(); disposed = true; memory.Dispose(); }
    }
}
