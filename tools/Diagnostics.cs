using System;
using System.Globalization;
using System.Threading;
using System.Web.Script.Serialization;
using Ets2Trainer;

internal static class Diagnostics
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        try
        {
            int seconds = args.Length > 0 ? Int32.Parse(args[0], CultureInfo.InvariantCulture) : 0;
            string mode = args.Length > 1 ? args[1] : "off";
            using (var m = ProcessMemory.Attach())
            using (var engine = new TrainerEngine(m))
            {
                engine.SetFuel(mode == "fuel" || mode == "both" || mode=="all");
                engine.SetFatigue(mode == "fatigue" || mode == "both" || mode=="all");
                engine.SetStability(mode == "natural" ? StabilityMode.Natural : mode == "strong" ? StabilityMode.Strong : mode=="stability"||mode=="all"?StabilityMode.Natural|StabilityMode.Strong:StabilityMode.Off);
                var clock = System.Diagnostics.Stopwatch.StartNew();
                long lastSecond = -1;
                do
                {
                    engine.Tick();
                    long elapsed = clock.ElapsedMilliseconds / 1000;
                    if (elapsed != lastSecond)
                    {
                        Print(m, engine, clock.Elapsed.TotalSeconds);
                        lastSecond = elapsed;
                    }
                    if (clock.Elapsed.TotalSeconds >= seconds) break;
                    Thread.Sleep(100);
                } while (true);
                engine.DisableAll();
                engine.Tick();
                Print(m, engine, clock.Elapsed.TotalSeconds);
                return String.IsNullOrEmpty(engine.Error) ? 0 : 2;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    private static void Print(ProcessMemory m, TrainerEngine e, double elapsed)
    {
        var s = e.Snapshot;
        var physics=e.StabilityRequested==StabilityMode.Off?StabilityReader.Read(m,s):e.Stability;
        Console.WriteLine(new JavaScriptSerializer().Serialize(new {
            time = DateTimeOffset.Now.ToString("o"), elapsed, pid = m.ProcessId,
            module = m.BaseAddress.ToString("X"), state = s.State,
            appState = s.AppState, pause = s.PauseCount, gameMinutes = s.GameMinutes,
            fuel = s.Fuel, fatigue = s.Fatigue, fuelActive = e.FuelActive,
            fatigueActive = e.FatigueActive, fuelWrites = e.FuelWrites,
            fatigueWrites = e.FatigueWrites, fuelIssue = s.FuelIssue,
            fatigueIssue = s.FatigueIssue, error = e.Error,
            stabilityMode=e.StabilityRequested.ToString(),stabilityActive=e.StabilityActive,
            corrections=e.StabilityCorrections,rightings=e.RightingCount,truckRoll=physics.TruckRoll,
            trailerRoll=physics.TrailerRoll,stabilityIssue=physics.Issue
        }));
    }
}
