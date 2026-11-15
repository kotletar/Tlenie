using System;
using System.Collections.Generic;
using PokaGoryu;

public static class CoreChecks
{
    static int checks;
    static void Assert(bool ok, string label)
    {
        checks++;
        if (!ok) throw new Exception("FAIL " + label);
    }
    static Pt Center(CellPos p) { return CityMap.CenterOf(p.X, p.Z); }
    static RunRecord Rec(int mode, int difficulty, float seconds, int cities, int returns, int saved)
    {
        RunRecord r = new RunRecord();
        r.Mode = mode; r.Difficulty = difficulty; r.Seconds = seconds; r.Cities = cities; r.Returns = returns; r.Saved = saved;
        r.Date = "2026-11-15";
        return r;
    }
    static void Landmarks(List<CellPos> items, string name, Pt start)
    {
        foreach (CellPos p in items)
        {
            Assert(CityMap.IsRoadCell(p.X, p.Z), name + " on a road");
            Assert(CityMap.FindPath(start, Center(p), true, 0f) != null, name + " reachable after burning walls");
        }
    }
    public static int Main()
    {
        try
        {
            GameConfig.ResetDefaults();
            Assert(GameConfig.FinalTime == 60f, "the final stand at the bus lasts exactly one minute (the music track is cut to it)");
            CityMap.Build();
            Pt start = CityMap.CenterOf(CityMap.StartX, CityMap.StartZ);
            Pt bus = CityMap.CenterOf(CityMap.BusX, CityMap.BusZ);
            Assert(CityMap.Survivors.Count == 10 && CityMap.Pizzas.Count == 8 && CityMap.Hearths.Count == 4, "landmark counts");
            Assert(CityMap.FindPath(start, bus, true, 0f) != null, "route to bus exists");
            Assert(CityMap.FindPath(start, bus, false, 0f) == null, "W1 gates route to bus");
            Landmarks(CityMap.Survivors, "survivor", start);
            Landmarks(CityMap.Pizzas, "pizza", start);
            Landmarks(CityMap.Hearths, "hearth", start);
            Landmarks(CityMap.Generators, "generator", start);
            int roads = 0;
            for (int x = 0; x < CityMap.W; x++)
                for (int z = 0; z < CityMap.H; z++)
                    if (CityMap.IsRoadCell(x, z))
                    {
                        roads++;
                        Assert(CityMap.DistBus[x, z] < 1e8f, "finite bus distance at " + x + "," + z);
                    }
            CellRect w1 = CityMap.Walls[0], w2 = CityMap.Walls[1];
            Assert(CityMap.FindPath(CityMap.CenterOf(w1.X0 - 1, w1.Z0), CityMap.CenterOf(w1.X1 + 1, w1.Z0), false, 0f) == null,
                "W1 has no bypass");
            Assert(CityMap.FindPath(CityMap.CenterOf(w2.X0 - 1, w2.Z0), CityMap.CenterOf(w2.X1 + 1, w2.Z0), false, 0f) != null,
                "W2 has a bypass");
            CityMap.Build();
            Assert(CityMap.Survivors.Count == 10 && CityMap.Pizzas.Count == 8, "rebuild does not duplicate landmarks");

            LightLedger ledger = new LightLedger();
            ledger.Reset(75);
            ledger.Record(LightReason.Lamp, 10, 0);
            ledger.Record(LightReason.Shot, -5, 30);
            ledger.Record(LightReason.Pizza, 2, 60);
            Assert(ledger.ExpectedLight == 82 && ledger.TotalIncome == 12 && ledger.TotalExpense == 5, "conservation of light");
            Assert(ledger.Amount(LightReason.Lamp, true, true) == 0 && ledger.Amount(LightReason.Pizza, true, true) == 2,
                "rolling minute expires old income");
            Assert(ledger.Amount(LightReason.Shot, false, true) == 5, "rolling minute retains recent expense");
            ledger.Tick(100);
            Assert(ledger.Amount(LightReason.Shot, false, true) == 0 && ledger.TotalExpense == 5, "expiry preserves run totals");
            ledger.Tick(10000);
            Assert(ledger.Amount(LightReason.Pizza, true, true) == 0, "long jumps expire the entire minute");
            ledger.Reset(55);
            Assert(ledger.ExpectedLight == 55 && ledger.TotalIncome == 0 && ledger.TotalExpense == 0, "new run clears ledger");
            bool rejected = false;
            try { ledger.Record(LightReason.Other, double.NaN, 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
            Assert(rejected, "non-finite telemetry rejected");

            GameDifficulty.Mode = Challenge.Standard;
            Assert(GameDifficulty.DrainScale == 1 && GameDifficulty.OrbScale == 1 && GameDifficulty.PressureScale == 1,
                "standard preserves all difficulty multipliers");
            Assert(Math.Abs(GameConfig.SmolderPerSec + GameConfig.AuraCostK * 25 - 1.24) < 0.0001, "golden passive drain 1.24/s");
            GameDifficulty.Mode = Challenge.Gentle;
            Assert(GameDifficulty.DrainScale < 1 && GameDifficulty.OrbScale > 1 && GameDifficulty.PressureScale < 1,
                "gentle eases light economy and spawn pacing");
            GameDifficulty.Mode = Challenge.Harsh;
            Assert(GameDifficulty.DrainScale > 1 && GameDifficulty.OrbScale < 1 && GameDifficulty.PressureScale > 1,
                "harsh increases light pressure and spawn pacing");
            GameConfig.SmolderPerSec = 9;
            GameConfig.ResetDefaults();
            Assert(GameConfig.SmolderPerSec == 0.24f && GameConfig.BusNeed == 60 && GameConfig.PizzaLight == 24,
                "live balance reset restores baseline");
            GameDifficulty.Mode = Challenge.Standard;

            // Бесконечный город: первый город равен классике, дальше темнее
            GameDifficulty.City = 1;
            Assert(GameDifficulty.PressureScale == 1 && GameDifficulty.DrainScale == 1 && GameDifficulty.ExtraShades == 0
                && GameDifficulty.LampWearBonus == 0, "first endless city equals the classic balance");
            GameDifficulty.City = 5;
            Assert(Math.Abs(GameDifficulty.PressureScale - 1.6f) < 0.001f && Math.Abs(GameDifficulty.DrainScale - 1.12f) < 0.001f
                && GameDifficulty.ExtraShades == 2 && Math.Abs(GameDifficulty.LampWearBonus - 0.12) < 1e-9, "city 5 is darker and more dangerous");
            GameDifficulty.City = 100;
            Assert(GameDifficulty.LampWearBonus == 0.2, "lamp wear is capped");
            GameDifficulty.City = 1;

            // Таблица рекордов
            RunRecords t = new RunRecords();
            Assert(t.Submit(Rec(0, 1, 900f, 1, 1, 7)) == 1, "first record is first place");
            Assert(t.Submit(Rec(0, 1, 700f, 1, 3, 9)) == 1, "a faster classic run takes first place");
            Assert(t.Submit(Rec(0, 1, 800f, 1, 0, 9)) == 2, "a middle classic run is second");
            Assert(t.Submit(Rec(0, 1, 700f, 1, 1, 9)) == 1, "equal time: fewer returns to the hearth ranks higher");
            Assert(t.Submit(Rec(0, 2, 5000f, 1, 0, 2)) == 1, "every difficulty has its own table");
            Assert(t.Submit(Rec(1, 1, 3000f, 0, 0, 0)) == 0, "an endless run without a finished city is not recorded");
            Assert(t.Submit(Rec(1, 1, 3000f, 3, 5, 20)) == 1 && t.Submit(Rec(1, 1, 3500f, 3, 5, 20)) == 2
                && t.Submit(Rec(1, 1, 9000f, 4, 5, 20)) == 1, "endless: more cities first, then the faster run");
            RunRecords full = new RunRecords();
            for (int i = 0; i < 12; i++) full.Submit(Rec(0, 1, 1000f + i, 1, 0, 5));
            Assert(full.Top(0, 1).Count == RunRecords.Keep && Math.Abs(full.Top(0, 1)[RunRecords.Keep - 1].Seconds - 1009f) < 0.01f,
                "only the best ten are kept");
            Assert(full.Submit(Rec(0, 1, 5000f, 1, 0, 5)) == 0 && full.Count == RunRecords.Keep, "a slow run outside the top ten is dropped");
            RunRecords back = RunRecords.Parse(t.Serialize());
            Assert(back.Count == t.Count, "records survive a save and load");
            List<RunRecord> was = t.Top(0, 1), now = back.Top(0, 1);
            bool same = was.Count == now.Count;
            for (int i = 0; same && i < was.Count; i++)
                same = Math.Abs(was[i].Seconds - now[i].Seconds) < 0.06f && was[i].Returns == now[i].Returns && was[i].Saved == now[i].Saved && was[i].Date == now[i].Date;
            Assert(same, "order and fields are preserved");
            Assert(RunRecords.Parse("garbage\n1;2;x;4;5;6;d\n0;1;-5;1;0;0;d\n9;9;1;1;1;1;d\n0;1;NaN;1;0;0;d").Count == 0, "damaged lines are skipped");
            Assert(RunRecords.Parse(null).Count == 0 && RunRecords.Parse("").Count == 0, "an empty file is an empty table");
            Assert(RunRecords.Clock(65f) == "1:05" && RunRecords.Clock(3725f) == "1:02:05" && RunRecords.Clock(-3f) == "0:00", "clock format");

            Console.WriteLine("CORE CHECKS: OK; " + checks + " assertions, " + roads + " road cells");
            return 0;
        }
        catch (Exception e) { Console.WriteLine(e); return 1; }
    }
}
