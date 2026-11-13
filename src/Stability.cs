using System;
using System.Collections.Generic;
using System.IO;

namespace Ets2Trainer
{
    [Flags]
    public enum StabilityMode { Off=0, Natural=1, Strong=2 }

    internal struct Vector3
    {
        internal double X, Y, Z;
        internal Vector3(double x, double y, double z) { X=x; Y=y; Z=z; }
        internal double LengthSquared { get { return X*X+Y*Y+Z*Z; } }
        internal Vector3 Add(Vector3 b) { return new Vector3(X+b.X,Y+b.Y,Z+b.Z); }
        internal Vector3 Scale(double scale) { return new Vector3(X*scale,Y*scale,Z*scale); }
        internal byte[] Bytes()
        {
            var bytes=new byte[12];
            Buffer.BlockCopy(BitConverter.GetBytes((float)X),0,bytes,0,4);
            Buffer.BlockCopy(BitConverter.GetBytes((float)Y),0,bytes,4,4);
            Buffer.BlockCopy(BitConverter.GetBytes((float)Z),0,bytes,8,4);
            return bytes;
        }
    }
    internal struct Quaternion
    {
        internal double X,Y,Z,W;
        internal Quaternion(double x,double y,double z,double w) { X=x;Y=y;Z=z;W=w; }
        internal Quaternion Conjugate() { return new Quaternion(-X,-Y,-Z,W); }
        internal Vector3 Rotate(Vector3 v)
        {
            Quaternion r=Multiply(new Quaternion(v.X,v.Y,v.Z,0)).Multiply(Conjugate());
            return new Vector3(r.X,r.Y,r.Z);
        }
        internal byte[] Bytes()
        {
            var b=new byte[16];double[] values={X,Y,Z,W};
            for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes((float)values[i]),0,b,i*4,4);
            return b;
        }
        internal Quaternion WithoutRoll()
        {
            double angle=-Roll/2;
            return Multiply(new Quaternion(0,0,Math.Sin(angle),Math.Cos(angle))).Normalize();
        }
        internal Quaternion Multiply(Quaternion b)
        {
            return new Quaternion(W*b.X+X*b.W+Y*b.Z-Z*b.Y,
                W*b.Y-X*b.Z+Y*b.W+Z*b.X,W*b.Z+X*b.Y-Y*b.X+Z*b.W,
                W*b.W-X*b.X-Y*b.Y-Z*b.Z);
        }
        internal Quaternion Normalize()
        {
            double norm=X*X+Y*Y+Z*Z+W*W;
            if (Double.IsNaN(norm)||Double.IsInfinity(norm)||norm<0.98||norm>1.02)
                throw new IOException("车辆姿态数据异常");
            double scale=1/Math.Sqrt(norm);
            return new Quaternion(X*scale,Y*scale,Z*scale,W*scale);
        }
        internal Vector3 Forward { get { return new Vector3(2*(X*Z+W*Y),2*(Y*Z-W*X),1-2*(X*X+Y*Y)); } }
        internal double Roll { get { return Math.Atan2(2*(X*Y+W*Z),1-2*(X*X+Z*Z)); } }
    }

    internal static class RollController
    {
        internal static Vector3 Correct(Quaternion q, Vector3 omega, StabilityMode mode)
        {
            if (mode==StabilityMode.Off) return omega;
            q=q.Normalize();
            if (Double.IsNaN(omega.LengthSquared)||Double.IsInfinity(omega.LengthSquared)||omega.LengthSquared>400)
                throw new IOException("车辆角速度异常");
            Vector3 axis=q.Forward;
            double horizontal=axis.X*axis.X+axis.Z*axis.Z;
            if (horizontal<0.04) return omega; // Euler roll is undefined near vertical pitch.
            // Exclude yaw from the roll-rate estimate even on a gradient.
            double rate=(omega.X*axis.X+omega.Z*axis.Z)/horizontal;
            double roll=q.Roll;
            double dead=5*Math.PI/180;
            if (Math.Abs(roll)<=dead && Math.Abs(roll+rate*0.15)<=dead) return omega;
            double effective=Math.Sign(roll)*Math.Max(0,Math.Abs(roll)-dead);
            double gain=4;
            double damping=1;
            double limit=1.5;
            double target=Math.Max(-limit,Math.Min(limit,(1-damping)*rate-gain*effective));
            return omega.Add(axis.Scale(target-rate));
        }
    }

    internal sealed class RollBody
    {
        internal string Name;
        internal long Actor, Wrapper, Core, Scene;
        internal Quaternion Orientation;
        internal Vector3 AngularVelocity;
        internal bool Buffered;
        internal long HistoryArray;
        internal readonly List<HistoryPart> Parts=new List<HistoryPart>();
    }
    internal sealed class HistoryPart
    {
        internal long Address,Wrapper,Core;
        internal Quaternion Orientation,BodyToActor;
        internal Vector3 Position;
        internal byte[] Placement;
        internal bool Buffered;
    }
    public sealed class StabilitySnapshot
    {
        internal readonly List<RollBody> Bodies=new List<RollBody>();
        public string Issue { get; internal set; }
        public double? TruckRoll { get; internal set; }
        public double? TrailerRoll { get; internal set; }
        public bool Ready { get { return Bodies.Count>0 && String.IsNullOrEmpty(Issue); } }
        public StabilitySnapshot() { Issue=""; }
    }

    internal static class StabilityReader
    {
        internal const long WrapperType=0x249dfb0, CoreType=0x255b9a8, TrailerType=0x22d4978;
        private static uint U(IMemory m,long a) { return BitConverter.ToUInt32(m.Read(a,4),0); }
        private static long P(IMemory m,long a)
        {
            long v=BitConverter.ToInt64(m.Read(a,8),0);
            if(!GameReader.IsPointer(v)||(v&7)!=0)throw new IOException("等待车辆物理对象加载");
            return v;
        }
        private static void Type(IMemory m,long a,long rva)
        {
            if(!GameReader.IsPointer(a)||BitConverter.ToInt64(m.Read(a,8),0)!=m.BaseAddress+rva)
                throw new IOException("车辆物理对象类型不匹配");
        }
        private static Quaternion Q(IMemory m,long a)
        {
            byte[] b=m.Read(a,16);
            return new Quaternion(BitConverter.ToSingle(b,0),BitConverter.ToSingle(b,4),BitConverter.ToSingle(b,8),BitConverter.ToSingle(b,12)).Normalize();
        }
        private static Vector3 V(IMemory m,long a,double limit)
        {
            byte[] b=m.Read(a,12);
            var v=new Vector3(BitConverter.ToSingle(b,0),BitConverter.ToSingle(b,4),BitConverter.ToSingle(b,8));
            if(Double.IsNaN(v.LengthSquared)||Double.IsInfinity(v.LengthSquared)||v.LengthSquared>limit*limit)
                throw new IOException("车辆物理向量异常");
            return v;
        }
        internal static StabilitySnapshot Read(IMemory m,GameSnapshot game)
        {
            var state=new StabilitySnapshot();
            if(!game.Driving||game.Actor==0||game.Truck==0) { state.Issue="等待驾驶场景恢复";return state; }
            try
            {
                state.Bodies.Add(ReadBody(m,game.Actor,Build162.ActorType,"车头"));
                long trailer=BitConverter.ToInt64(m.Read(game.Truck+0xc8,8),0);
                if(trailer!=0)state.Bodies.Add(ReadBody(m,trailer,TrailerType,"挂车"));
                state.TruckRoll=state.Bodies[0].Orientation.Roll*180/Math.PI;
                if(state.Bodies.Count>1)state.TrailerRoll=state.Bodies[1].Orientation.Roll*180/Math.PI;
            }
            catch(IOException ex) { state.Bodies.Clear();state.Issue=ex.Message; }
            return state;
        }
        private static RollBody ReadBody(IMemory m,long actor,long actorType,string name)
        {
            Type(m,actor,actorType);
            long wrapper=P(m,actor+0x30);Type(m,wrapper,WrapperType);
            long core=P(m,wrapper+0xf8);Type(m,core,CoreType);
            if((U(m,core+0x68)>>30)!=2 || (m.Read(core+0xac,1)[0]&1)!=0)
                throw new IOException("车辆物理状态尚未就绪");
            uint flags=U(m,core+0x17c);
            long scene=P(m,core+0x60);
            Quaternion bodyToActor=Q(m,core+0xb0);
            Quaternion orientation=Q(m,core+0x90).Multiply(bodyToActor.Conjugate()).Normalize();
            byte[] velocity=m.Read(core+0xe0,12);
            var omega=new Vector3(BitConverter.ToSingle(velocity,0),BitConverter.ToSingle(velocity,4),BitConverter.ToSingle(velocity,8));
            if(Double.IsNaN(omega.LengthSquared)||Double.IsInfinity(omega.LengthSquared)||omega.LengthSquared>400)
                throw new IOException("车辆角速度异常");
            var body=new RollBody {Name=name,Actor=actor,Wrapper=wrapper,Core=core,Scene=scene,
                Orientation=orientation,AngularVelocity=omega,
                Buffered=(flags&0xa00200)!=0 || m.Read(scene+0x2009,1)[0]!=0};
            body.HistoryArray=P(m,actor+0x218);
            long count=BitConverter.ToInt64(m.Read(actor+0x220,8),0);
            if(count<1||count>16)throw new IOException("车辆物理缓冲数量不匹配");
            var cores=new HashSet<long>();bool primary=false;
            for(int i=0;i<count;i++)
            {
                long h=P(m,body.HistoryArray+i*8),w=P(m,h);Type(m,w,WrapperType);
                long c=P(m,w+0xf8);Type(m,c,CoreType);
                if(!cores.Add(c)||P(m,c+0x60)!=scene||(U(m,c+0x68)>>30)!=2||(m.Read(c+0xac,1)[0]&1)!=0)
                    throw new IOException("车辆物理缓冲对象不匹配");
                byte[] placement=m.Read(h+0x10,32);
                var q=new Quaternion(BitConverter.ToSingle(placement,20),BitConverter.ToSingle(placement,24),BitConverter.ToSingle(placement,28),BitConverter.ToSingle(placement,16)).Normalize();
                var previous=m.Read(h+0x58,16);
                new Quaternion(BitConverter.ToSingle(previous,4),BitConverter.ToSingle(previous,8),BitConverter.ToSingle(previous,12),BitConverter.ToSingle(previous,0)).Normalize();
                V(m,h+0x10,30000);V(m,h+0x48,30000);V(m,h+0x3c,20);V(m,h+0x74,20);
                Vector3 pos=V(m,c+0xa0,30000);
                if(w==wrapper)primary=true;
                body.Parts.Add(new HistoryPart{Address=h,Wrapper=w,Core=c,Orientation=q,BodyToActor=Q(m,c+0xb0),Position=pos,Placement=placement,
                    Buffered=(U(m,c+0x17c)&0xa00200)!=0 || m.Read(scene+0x2009,1)[0]!=0});
            }
            if(!primary)throw new IOException("找不到车身物理缓冲");
            return body;
        }
        internal static int Apply(IMemory m,GameSnapshot initial,StabilitySnapshot expected,StabilityMode mode,bool allowRighting,out bool righted)
        {
            int count=0;righted=false;
            bool recover=(mode&StabilityMode.Strong)!=0 && allowRighting && expected.Bodies.Exists(delegate(RollBody b){return Math.Abs(b.Orientation.Roll)>35*Math.PI/180;});
            if(recover && expected.Bodies.Exists(delegate(RollBody b){return b.Buffered||b.Parts.Exists(delegate(HistoryPart p){return p.Buffered;});}))return 0;
            foreach(var body in expected.Bodies)
            {
                var game=GameReader.Read(m);
                if(!game.Driving||game.Application!=initial.Application||game.World!=initial.World||game.Truck!=initial.Truck||game.Actor!=initial.Actor)return count;
                var fresh=Read(m,game);
                if(!fresh.Ready||fresh.Bodies.Count!=expected.Bodies.Count)return count;
                RollBody target=fresh.Bodies.Find(delegate(RollBody b){return b.Actor==body.Actor && b.Wrapper==body.Wrapper && b.Core==body.Core && b.Scene==body.Scene;});
                if(target==null||target.Buffered||target.HistoryArray!=body.HistoryArray||target.Parts.Count!=body.Parts.Count)continue;
                bool same=true;
                for(int i=0;i<body.Parts.Count;i++)if(body.Parts[i].Address!=target.Parts[i].Address||body.Parts[i].Core!=target.Parts[i].Core||body.Parts[i].Wrapper!=target.Parts[i].Wrapper||target.Parts[i].Buffered)same=false;
                if(!same)continue;
                Vector3 axis=target.Orientation.Forward;
                if(axis.X*axis.X+axis.Z*axis.Z<0.04)continue;
                if(recover)
                {
                    // SCS restores pose/velocities from both history slots each frame.
                    // Correct every part, including the suspended cabin, as one rigid transform.
                    Quaternion delta=target.Orientation.WithoutRoll().Multiply(target.Orientation.Conjugate()).Normalize();
                    HistoryPart main=target.Parts.Find(delegate(HistoryPart p){return p.Core==target.Core;});
                    double lift=Math.Abs(target.Orientation.Roll)>70*Math.PI/180?0.8:0.3;
                    foreach(var p in target.Parts)
                    {
                        Quaternion q=delta.Multiply(p.Orientation).Normalize();
                        Vector3 relative=p.Position.Add(main.Position.Scale(-1));
                        Vector3 newPosition=main.Position.Add(delta.Rotate(relative)).Add(new Vector3(0,lift,0));
                        Vector3 shift=newPosition.Add(p.Position.Scale(-1));
                        byte[] placement=(byte[])p.Placement.Clone();
                        for(int i=0;i<3;i++)
                        {
                            double component=i==0?shift.X:i==1?shift.Y:shift.Z;
                            Buffer.BlockCopy(BitConverter.GetBytes((float)(BitConverter.ToSingle(placement,i*4)+component)),0,placement,i*4,4);
                        }
                        double[] fields={q.W,q.X,q.Y,q.Z};
                        for(int i=0;i<4;i++)Buffer.BlockCopy(BitConverter.GetBytes((float)fields[i]),0,placement,16+i*4,4);
                        m.Write(p.Address+0x10,placement);m.Write(p.Address+0x48,placement);
                        m.Write(p.Address+0x30,new byte[24]);m.Write(p.Address+0x68,new byte[24]);
                        byte[] nativeQ=q.Multiply(p.BodyToActor).Normalize().Bytes();
                        m.Write(p.Core+0x90,nativeQ);m.Write(p.Core+0x140,nativeQ);
                        m.Write(p.Core+0xa0,newPosition.Bytes());m.Write(p.Core+0x150,newPosition.Bytes());
                        m.Write(p.Core+0xd0,new byte[12]);m.Write(p.Core+0x15c,new byte[12]);
                        m.Write(p.Core+0xe0,new byte[12]);m.Write(p.Core+0x168,new byte[12]);
                    }
                    count++;righted=true;continue;
                }
                if((mode&StabilityMode.Natural)==0)continue;
                Vector3 desired=RollController.Correct(target.Orientation,target.AngularVelocity,StabilityMode.Natural);
                Vector3 difference=desired.Add(target.AngularVelocity.Scale(-1));
                if(difference.LengthSquared<1e-12)continue;
                foreach(var p in target.Parts)
                {
                    Vector3 omega=V(m,p.Core+0xe0,20).Add(difference);
                    m.Write(p.Address+0x3c,omega.Bytes());m.Write(p.Address+0x74,omega.Bytes());
                    m.Write(p.Core+0xe0,omega.Bytes());m.Write(p.Core+0x168,omega.Bytes());
                }
                count++;
            }
            return count;
        }
    }
}
