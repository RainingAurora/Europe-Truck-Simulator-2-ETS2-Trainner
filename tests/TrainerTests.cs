using System;
using System.Collections.Generic;
using System.IO;
using Ets2Trainer;

internal sealed class FakeMemory : IMemory
{
    internal readonly Dictionary<long, byte> Bytes = new Dictionary<long, byte>();
    internal Action<long> BeforeRead;
    internal bool FailWrite;
    internal int Writes;
    private readonly long moduleBase;
    public long BaseAddress { get { return moduleBase; } }
    public bool IsAlive { get; set; }
    internal const long App = 0x20000000, World = 0x21000000, Economy = 0x22000000;
    internal const long Truck = 0x23000000, Actor = 0x24000000, Vehicle = 0x25000000, Driver = 0x26000000;
    internal FakeMemory(long baseAddress = 0x140000000)
    {
        moduleBase = baseAddress;
        IsAlive = true;
        Set(BaseAddress + Build162.AppRoot, BitConverter.GetBytes(App));
        Set(BaseAddress + Build162.WorldRoot, BitConverter.GetBytes(World));
        Set(BaseAddress + Build162.EconomyRoot, BitConverter.GetBytes(Economy));
        Type(App, Build162.AppType); Type(World, Build162.WorldType); Type(Economy, Build162.EconomyType);
        Type(Truck, Build162.TruckType); Type(Actor, Build162.ActorType);
        Type(Vehicle, Build162.VehicleType); Type(Driver, Build162.DriverType);
        U(App+0x594, 2); U(App+0xac0, 0); U(World+8, 0xa2000001); U(Economy+8, 0xa2000001);
        Set(World+0x31b0, BitConverter.GetBytes(Truck)); Set(Truck+0x18, BitConverter.GetBytes(Actor));
        Set(Actor+0x1f8, BitConverter.GetBytes(Vehicle)); Set(Economy+0x18, BitConverter.GetBytes(Driver));
        Set(Vehicle+0x1c0, BitConverter.GetBytes(0.03f)); U(Driver+0x50, 650); U(Economy+0x19c, 12345);
    }
    internal void Type(long at, long rva) { Set(at, BitConverter.GetBytes(BaseAddress+rva)); }
    internal void U(long at, uint value) { Set(at, BitConverter.GetBytes(value)); }
    internal void Set(long at, byte[] value) { for (int i=0;i<value.Length;i++) Bytes[at+i]=value[i]; }
    public byte[] Read(long at, int count)
    {
        if (BeforeRead!=null) BeforeRead(at);
        var result=new byte[count];
        for(int i=0;i<count;i++) { byte value; if(!Bytes.TryGetValue(at+i,out value)) throw new IOException("unmapped"); result[i]=value; }
        return result;
    }
    public void Write(long at, byte[] value) { if(FailWrite) throw new IOException("write denied"); Writes++;Set(at,value); }
    public void Dispose() { IsAlive=false; }
}

internal static class TrainerTests
{
    private static int passed;
    private static void Check(bool condition,string name) { if(!condition)throw new Exception(name);passed++;Console.WriteLine("PASS "+name); }
    private static Quaternion Axis(double x,double y,double z,double angle) { double s=Math.Sin(angle/2);return new Quaternion(x*s,y*s,z*s,Math.Cos(angle/2)); }
    private static void PutQuaternion(FakeMemory m,long address,Quaternion q)
    {
        var b=new byte[16];double[] values={q.X,q.Y,q.Z,q.W};
        for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes((float)values[i]),0,b,i*4,4);
        m.Set(address,b);
    }
    private static void PhysicsBody(FakeMemory m,long actor,long wrapper,long core,double degrees)
    {
        m.Set(actor+0x30,BitConverter.GetBytes(wrapper));m.Type(wrapper,StabilityReader.WrapperType);
        m.Set(wrapper+0xf8,BitConverter.GetBytes(core));m.Type(core,StabilityReader.CoreType);
        m.U(core+0x68,0x83000000);m.Set(core+0xac,new byte[]{0});m.U(core+0x17c,0);
        m.Set(core+0x60,BitConverter.GetBytes((long)0x29000000));m.Set(0x29002009,new byte[]{0});
        PutQuaternion(m,core+0x90,Axis(0,0,1,degrees*Math.PI/180));
        PutQuaternion(m,core+0xb0,new Quaternion(0,0,0,1));
        m.Set(core+0xe0,new Vector3(0,.3,.4).Bytes());m.Set(core+0x168,new Vector3(0,.3,.4).Bytes());
        m.Set(core+0xa0,new Vector3(0,20,0).Bytes());
        long array=core+0x1000,history=core+0x2000;
        m.Set(actor+0x218,BitConverter.GetBytes(array));m.Set(actor+0x220,BitConverter.GetBytes((long)1));
        m.Set(array,BitConverter.GetBytes(history));m.Set(history,new byte[0xa0]);m.Set(history,BitConverter.GetBytes(wrapper));
        var q=Axis(0,0,1,degrees*Math.PI/180);
        foreach(long offset in new long[]{0x10,0x48})
        {
            m.Set(history+offset,new Vector3(0,20,0).Bytes());
            PutQuaternion(m,history+offset+16,new Quaternion(q.W,q.X,q.Y,q.Z));
        }
        m.Set(history+0x3c,new Vector3(0,.3,.4).Bytes());m.Set(history+0x74,new Vector3(0,.3,.4).Bytes());
    }
    private static FakeMemory Physics(bool trailer=false)
    {
        var m=new FakeMemory();PhysicsBody(m,FakeMemory.Actor,0x27000000,0x28000000,30);
        m.Set(FakeMemory.Truck+0xc8,BitConverter.GetBytes(trailer?(long)0x2a000000:0));
        if(trailer) {m.Type(0x2a000000,StabilityReader.TrailerType);PhysicsBody(m,0x2a000000,0x2b000000,0x2c000000,-30);}
        return m;
    }
    public static int Main()
    {
        try
        {
            Check(Build162.IsSupportedHash(Build162.Sha256.ToLowerInvariant()),"supported exact build");
            Check(!Build162.IsSupportedHash(new String('0',64)),"unknown build rejected");
            Check(!Build162.IsSupportedHash(null),"missing build rejected");
            var m=new FakeMemory();var e=new TrainerEngine(m);e.Tick();
            Check(m.Writes==0 && e.Snapshot.CanFuel && e.Snapshot.CanFatigue,"disabled defaults read without writing");
            e.SetFuel(true);e.Tick();
            Check(e.Snapshot.Fuel==1 && e.Snapshot.Fatigue==650 && e.FuelActive && !e.FatigueActive,"low fuel fills independently");
            e.SetFuel(false);e.SetFatigue(true);e.Tick();
            Check(e.Snapshot.Fatigue==0 && e.FuelWrites==1 && e.FatigueActive,"fatigue clears independently");
            e.DisableAll();int count=m.Writes;e.Tick();Check(m.Writes==count,"all off stops writes");
            e.SetFuel(true);e.SetFatigue(true);m.U(FakeMemory.App+0xac0,1);e.Tick();
            Check(m.Writes==count && !e.FuelActive && !e.FatigueActive,"paused game suspends both");
            m.U(FakeMemory.App+0x594,1);m.U(FakeMemory.App+0xac0,0);e.Tick();
            Check(m.Writes==count,"main menu never writes");
            m.U(FakeMemory.App+0x594,2);e.Tick();Check(m.Writes==count+2,"resume validates and reapplies desired switches");
            m=new FakeMemory();e=new TrainerEngine(m);m.Set(FakeMemory.Vehicle+0x1c0,BitConverter.GetBytes(Single.NaN));
            e.SetFuel(true);e.SetFatigue(true);e.Tick();
            Check(e.FuelWrites==0 && e.FatigueWrites==1,"invalid fuel blocks fuel while fatigue remains independent");
            m=new FakeMemory();e=new TrainerEngine(m);m.Type(FakeMemory.Driver,Build162.DriverType+8);e.SetFatigue(true);e.Tick();
            Check(m.Writes==0,"wrong driver type never written");
            m=new FakeMemory();e=new TrainerEngine(m);m.Set(FakeMemory.Vehicle+0x1c0,BitConverter.GetBytes(1.5f));e.SetFuel(true);e.Tick();
            Check(m.Writes==0,"out of range fuel rejected");
            m=new FakeMemory();e=new TrainerEngine(m);m.U(FakeMemory.Driver+0x50,UInt32.MaxValue);e.SetFatigue(true);e.Tick();
            Check(m.Writes==0,"out of range fatigue rejected");
            m=new FakeMemory();e=new TrainerEngine(m);m.Set(m.BaseAddress+Build162.WorldRoot,BitConverter.GetBytes((long)0));e.SetFuel(true);e.Tick();
            Check(m.Writes==0,"null pointer during load never written");
            m=new FakeMemory();e=new TrainerEngine(m);e.SetFuel(true);int reads=0;
            m.BeforeRead=delegate(long address) {
                if(address==m.BaseAddress+Build162.AppRoot && ++reads==2) m.U(FakeMemory.App+0xac0,1);
            };
            e.Tick();Check(m.Writes==0,"pause between initial read and write blocks stale action");
            m=new FakeMemory();e=new TrainerEngine(m);e.SetFuel(true);reads=0;
            m.BeforeRead=delegate(long address) {
                if(address==m.BaseAddress+Build162.WorldRoot && ++reads==2) m.Set(FakeMemory.Actor+0x1f8,BitConverter.GetBytes((long)0));
            };
            e.Tick();Check(m.Writes==0,"vehicle invalidated during revalidation never written");
            m=new FakeMemory();e=new TrainerEngine(m);e.SetFuel(true);e.SetFatigue(true);m.IsAlive=false;e.Tick();
            Check(m.Writes==0 && !e.FuelRequested && !e.FatigueRequested,"process exit clears both switches");
            m=new FakeMemory();e=new TrainerEngine(m);e.SetFuel(true);m.FailWrite=true;e.Tick();
            Check(!e.FuelRequested && !e.FatigueRequested && e.Error.Length>0,"write failure stops modification");
            m=new FakeMemory();e=new TrainerEngine(m);e.SetFuel(true);e.Dispose();e.Tick();e.SetFuel(true);e.Tick();
            Check(m.Writes==0 && !e.FuelRequested,"dispose prevents further writes or reenable");
            m=new FakeMemory(0x7ff700000000);e=new TrainerEngine(m);e.SetFuel(true);e.SetFatigue(true);e.Tick();
            Check(e.Snapshot.Fuel==1 && e.Snapshot.Fatigue==0 && m.Writes==2,"module relocation resolves both chains");
            m=new FakeMemory();e=new TrainerEngine(m);e.SetFatigue(true);e.Tick();
            m.Type(0x27000000,Build162.DriverType);m.U(0x27000050,123);
            m.Set(FakeMemory.Economy+0x18,BitConverter.GetBytes((long)0x27000000));e.Tick();
            Check(e.Snapshot.Driver==0x27000000 && e.Snapshot.Fatigue==0,"reload rebinds a fresh valid driver");
            m=new FakeMemory();e=new TrainerEngine(m);e.SetFuel(true);e.Tick();
            m.Type(0x28000000,Build162.VehicleType);m.Set(0x280001c0,BitConverter.GetBytes(0.2f));
            m.Set(FakeMemory.Actor+0x1f8,BitConverter.GetBytes((long)0x28000000));e.Tick();
            Check(e.Snapshot.Vehicle==0x28000000 && e.Snapshot.Fuel==1,"vehicle switch rebinds a fresh valid fuel target");
            var corrected=RollController.Correct(Axis(0,0,1,3*Math.PI/180),new Vector3(0,.7,0),StabilityMode.Natural);
            Check(corrected.Y==.7 && corrected.Z==0,"natural mode preserves small normal body lean");
            corrected=RollController.Correct(Axis(0,0,1,15*Math.PI/180),new Vector3(0,.7,0),StabilityMode.Natural);
            Check(corrected.Z<0 && corrected.Y==.7,"natural mode opposes excessive lean while preserving yaw");
            corrected=RollController.Correct(Axis(0,0,1,-45*Math.PI/180),new Vector3(0,0,0),StabilityMode.Natural);
            Check(Math.Abs(corrected.Z-1.5)<1e-8,"natural correction is bounded and has the correct reverse direction");
            double yaw=1.2,pitch=.3,roll=.3;
            var q=Axis(0,1,0,yaw).Multiply(Axis(1,0,0,pitch)).Multiply(Axis(0,0,1,roll));
            var axis=q.Forward;
            var original=new Vector3(.2*Math.Cos(yaw),.7,-.2*Math.Sin(yaw)).Add(axis.Scale(.4));
            corrected=RollController.Correct(q,original,StabilityMode.Strong);
            double newRate=(corrected.X*axis.X+corrected.Z*axis.Z)/(axis.X*axis.X+axis.Z*axis.Z);
            double newYaw=corrected.Y-axis.Y*newRate;
            double newPitch=corrected.X*Math.Cos(yaw)-corrected.Z*Math.Sin(yaw);
            Check(Math.Abs(newYaw-.7)<1e-8 && Math.Abs(newPitch-.2)<1e-8,"roll correction preserves yaw and pitch on gradients");
            Check(Math.Abs(q.Roll-roll)<1e-8,"roll angle is invariant to heading and normal pitch");
            original=new Vector3(.2,.3,.4);
            corrected=RollController.Correct(Axis(1,0,0,Math.PI/2),original,StabilityMode.Strong);
            Check(corrected.X==original.X && corrected.Y==original.Y && corrected.Z==original.Z,"near vertical pitch avoids undefined roll correction");
            bool rejected=false;try{RollController.Correct(new Quaternion(0,0,0,0),original,StabilityMode.Strong);}catch(IOException){rejected=true;}
            Check(rejected,"invalid physics quaternion rejected");
            m=Physics(true);e=new TrainerEngine(m);e.SetFuel(true);e.SetFatigue(true);
            e.SetStability(StabilityMode.Natural|StabilityMode.Strong);
            Check(e.StabilityRequested==(StabilityMode.Natural|StabilityMode.Strong) && e.FuelRequested && e.FatigueRequested,"both stability features can be enabled alongside fuel and fatigue");
            e.SetStability(e.StabilityRequested^StabilityMode.Strong);
            Check(e.StabilityRequested==StabilityMode.Natural,"turning off strong leaves natural enabled");
            e.SetFuel(false);e.SetFatigue(false);e.Tick();
            Check(e.StabilityActive && e.StabilityCorrections==2 && m.Writes==8,"stability controls both native bodies and SCS history slots");
            Check(BitConverter.ToSingle(m.Read(0x280000e0+8,4),0)<0 && BitConverter.ToSingle(m.Read(0x2c0000e0+8,4),0)>0,"opposite truck and trailer lean receive opposite corrections");
            e.DisableAll();count=m.Writes;e.Tick();Check(m.Writes==count && e.StabilityRequested==StabilityMode.Off,"all off also disables stability writes");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);m.U(FakeMemory.App+0xac0,1);e.Tick();
            Check(m.Writes==0 && !e.StabilityActive,"paused game blocks stabilization");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);m.Set(0x29002009,new byte[]{1});e.Tick();
            Check(m.Writes==0,"physics simulation buffering blocks direct velocity writes");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);m.Type(0x28000000,StabilityReader.CoreType+8);e.Tick();
            Check(m.Writes==0 && e.Stability.Issue.Length>0,"unexpected rigid body type blocks stabilization");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);PutQuaternion(m,0x28000090,new Quaternion(0,0,0,0));e.Tick();
            Check(m.Writes==0 && e.Stability.Issue.Length>0,"invalid live orientation blocks stabilization");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);reads=0;
            m.BeforeRead=delegate(long address){if(address==FakeMemory.Actor+0x30 && ++reads==2)m.Set(address,BitConverter.GetBytes((long)0));};
            e.Tick();Check(m.Writes==0,"physics object invalidated before correction is not written");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);m.IsAlive=false;e.Tick();
            Check(e.StabilityRequested==StabilityMode.Off && m.Writes==0,"process exit clears the selected stability mode");
            m=Physics(true);PhysicsBody(m,FakeMemory.Actor,0x27000000,0x28000000,90);PhysicsBody(m,0x2a000000,0x2b000000,0x2c000000,-80);
            e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);e.Tick();
            Check(e.RightingCount==1 && e.StabilityCorrections==2 && e.StabilityActive,"strong mode recovers both sides of a rolled assembly");
            var recovered=StabilityReader.Read(m,GameReader.Read(m));
            Check(Math.Abs(recovered.TruckRoll.Value)<.001 && Math.Abs(recovered.TrailerRoll.Value)<.001,"righting resets actual and historical roll");
            Check(BitConverter.ToSingle(m.Read(0x28002014,4),0)>20 && BitConverter.ToSingle(m.Read(0x280000e8,4),0)==0,"righting provides ground clearance and stops angular motion");
            q=Axis(0,1,0,1.2).Multiply(Axis(1,0,0,.25)).Multiply(Axis(0,0,1,1.6));
            var upright=q.WithoutRoll();var beforeForward=q.Forward;var afterForward=upright.Forward;
            Check(Math.Abs(upright.Roll)<1e-8 && afterForward.Add(beforeForward.Scale(-1)).LengthSquared<1e-16,"pose righting preserves heading and road pitch");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Strong);e.Tick();
            Check(m.Writes==0 && e.RightingCount==0,"strong mode is armed without modifying normal cornering below threshold");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Natural);m.Set(0x28001000,BitConverter.GetBytes((long)0));e.Tick();
            Check(m.Writes==0 && e.Stability.Issue.Length>0,"missing history is rejected before physics writes");
            m=Physics();e=new TrainerEngine(m);e.SetStability(StabilityMode.Natural);m.Set(FakeMemory.Actor+0x220,BitConverter.GetBytes((long)100));e.Tick();
            Check(m.Writes==0,"unbounded history count is rejected");
            Console.WriteLine("Passed "+passed+" checks.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine("FAIL "+ex.Message);return 1;}
    }
}
