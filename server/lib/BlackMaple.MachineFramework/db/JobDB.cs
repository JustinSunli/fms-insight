/* Copyright (c) 2021, John Lenz

All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright
      notice, this list of conditions and the following disclaimer.

    * Redistributions in binary form must reproduce the above
      copyright notice, this list of conditions and the following
      disclaimer in the documentation and/or other materials provided
      with the distribution.

    * Neither the name of John Lenz, Black Maple Software, SeedTactics,
      nor the names of other contributors may be used to endorse or
      promote products derived from this software without specific
      prior written permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
"AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Data;
using Microsoft.Data.Sqlite;
using System.Collections.Immutable;

namespace BlackMaple.MachineFramework
{
  //database backend for the job db
  internal partial class Repository
  {
    #region "Loading Jobs"
    private record PathStopRow
    {
      public string StationGroup { get; init; } = "";
      public ImmutableList<int>.Builder Stations { get; } = ImmutableList.CreateBuilder<int>();
      public string Program { get; init; }
      public long? ProgramRevision { get; init; }
      public ImmutableDictionary<string, TimeSpan>.Builder Tools { get; } = ImmutableDictionary.CreateBuilder<string, TimeSpan>();
      public TimeSpan ExpectedCycleTime { get; init; }
    }

    private record HoldRow
    {
      public bool UserHold { get; init; }
      public string ReasonForUserHold { get; init; } = "";
      public ImmutableList<TimeSpan>.Builder HoldUnholdPattern { get; } = ImmutableList.CreateBuilder<TimeSpan>();
      public DateTime HoldUnholdPatternStartUTC { get; init; }
      public bool HoldUnholdPatternRepeats { get; init; }

      public HoldPattern ToHoldPattern()
      {
        return new HoldPattern()
        {
          UserHold = this.UserHold,
          ReasonForUserHold = this.ReasonForUserHold,
          HoldUnholdPattern = this.HoldUnholdPattern.ToImmutable(),
          HoldUnholdPatternRepeats = this.HoldUnholdPatternRepeats,
          HoldUnholdPatternStartUTC = this.HoldUnholdPatternStartUTC
        };
      }
    }

    private record PathDataRow
    {
      public int Process { get; init; }
      public int Path { get; init; }
      public DateTime StartingUTC { get; init; }
      public int PartsPerPallet { get; init; }
      public int PathGroup { get; init; }
      public TimeSpan SimAverageFlowTime { get; init; }
      public string InputQueue { get; init; }
      public string OutputQueue { get; init; }
      public TimeSpan LoadTime { get; init; }
      public TimeSpan UnloadTime { get; init; }
      public string Fixture { get; init; }
      public int? Face { get; init; }
      public string Casting { get; init; }
      public ImmutableList<int>.Builder Loads { get; } = ImmutableList.CreateBuilder<int>();
      public ImmutableList<int>.Builder Unloads { get; } = ImmutableList.CreateBuilder<int>();
      public ImmutableList<PathInspection>.Builder Insps { get; } = ImmutableList.CreateBuilder<PathInspection>();
      public ImmutableList<string>.Builder Pals { get; } = ImmutableList.CreateBuilder<string>();
      public ImmutableList<SimulatedProduction>.Builder SimProd { get; } = ImmutableList.CreateBuilder<SimulatedProduction>();
      public SortedList<int, PathStopRow> Stops { get; } = new SortedList<int, PathStopRow>();
      public HoldRow MachHold { get; set; } = null;
      public HoldRow LoadHold { get; set; } = null;
    }

    private record JobDetails
    {
      public ImmutableList<int> CyclesOnFirstProc { get; init; }
      public ImmutableList<string> Bookings { get; init; }
      public ImmutableList<ProcessInfo> Procs { get; init; }
      public HoldRow Hold { get; init; }
    }

    private JobDetails LoadJobData(string uniq, IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.Parameters.Add("$uniq", SqliteType.Text).Value = uniq;

        //read plan quantity
        cmd.CommandText = "SELECT Path, PlanQty FROM planqty WHERE UniqueStr = $uniq";
        var cyclesOnFirstProc = new SortedDictionary<int, int>();
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            cyclesOnFirstProc[reader.GetInt32(0)] = reader.GetInt32(1);
          }
        }

        //scheduled bookings
        cmd.CommandText = "SELECT BookingId FROM scheduled_bookings WHERE UniqueStr = $uniq";
        var bookings = ImmutableList.CreateBuilder<string>();
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            bookings.Add(reader.GetString(0));
          }
        }

        //path data
        var pathDatRows = new Dictionary<(int proc, int path), PathDataRow>();
        cmd.CommandText = "SELECT Process, Path, StartingUTC, PartsPerPallet, PathGroup, SimAverageFlowTime, InputQueue, OutputQueue, LoadTime, UnloadTime, Fixture, Face, Casting FROM pathdata WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var proc = reader.GetInt32(0);
            var path = reader.GetInt32(1);

            string fixture = null;
            int? face = null;
            if (!reader.IsDBNull(10) && !reader.IsDBNull(11))
            {
              var faceTy = reader.GetFieldType(11);
              if (faceTy == typeof(string))
              {
                if (int.TryParse(reader.GetString(11), out int f))
                {
                  fixture = reader.GetString(10);
                  face = f;
                }
              }
              else
              {
                fixture = reader.GetString(10);
                face = reader.GetInt32(11);
              }
            }


            pathDatRows[(proc, path)] = new PathDataRow()
            {
              Process = proc,
              Path = path,
              StartingUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
              PartsPerPallet = reader.GetInt32(3),
              PathGroup = reader.GetInt32(4),
              SimAverageFlowTime = reader.IsDBNull(5) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(5)),
              InputQueue = reader.IsDBNull(6) ? null : reader.GetString(6),
              OutputQueue = reader.IsDBNull(7) ? null : reader.GetString(7),
              LoadTime = reader.IsDBNull(8) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(8)),
              UnloadTime = reader.IsDBNull(9) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(9)),
              Fixture = fixture,
              Face = face,
              Casting = reader.IsDBNull(12) ? null : reader.GetString(12)
            };
          }
        }

        //read pallets
        cmd.CommandText = "SELECT Process, Path, Pallet FROM pallets WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            if (pathDatRows.TryGetValue((proc: reader.GetInt32(0), path: reader.GetInt32(1)), out var pathRow))
            {
              pathRow.Pals.Add(reader.GetString(2));
            }
          }
        }

        //simulated production
        cmd.CommandText = "SELECT Process, Path, TimeUTC, Quantity FROM simulated_production WHERE UniqueStr = $uniq ORDER BY Process,Path,TimeUTC";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              pathRow.SimProd.Add(new SimulatedProduction()
              {
                TimeUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
                Quantity = reader.GetInt32(3),
              });
            }
          }
        }


        //now add routes
        cmd.CommandText = "SELECT Process, Path, RouteNum, StatGroup, ExpectedCycleTime, Program, ProgramRevision FROM stops WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            int routeNum = reader.GetInt32(2);

            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              var stop = new PathStopRow()
              {
                StationGroup = reader.GetString(3),
                ExpectedCycleTime = reader.IsDBNull(4) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(4)),
                Program = reader.IsDBNull(5) ? null : reader.GetString(5),
                ProgramRevision = reader.IsDBNull(6) ? null : reader.GetInt64(6),
              };
              pathRow.Stops[routeNum] = stop;
            }
          }
        }

        //programs for routes
        cmd.CommandText = "SELECT Process, Path, RouteNum, StatNum FROM stops_stations WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            int routeNum = reader.GetInt32(2);
            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              if (pathRow.Stops.TryGetValue(routeNum, out var stop))
              {
                stop.Stations.Add(reader.GetInt32(3));
              }
            }
          }
        }

        //tools for routes
        cmd.CommandText = "SELECT Process, Path, RouteNum, Tool, ExpectedUse FROM tools WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            int routeNum = reader.GetInt32(2);
            if (pathDatRows.TryGetValue(key, out var pathRow))
            {
              if (pathRow.Stops.TryGetValue(routeNum, out var stop))
              {
                stop.Tools[reader.GetString(3)] = TimeSpan.FromTicks(reader.GetInt64(4));
              }
            }
          }
        }

        //now add load/unload
        cmd.CommandText = "SELECT Process, Path, StatNum, Load FROM loadunload WHERE UniqueStr = $uniq";
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var key = (proc: reader.GetInt32(0), path: reader.GetInt32(1));
            if (pathDatRows.TryGetValue(key, out var pathData))
            {
              if (reader.GetBoolean(3))
              {
                pathData.Loads.Add(reader.GetInt32(2));
              }
              else
              {
                pathData.Unloads.Add(reader.GetInt32(2));
              }
            }
          }
        }

        //now inspections
        cmd.CommandText = "SELECT Process, Path, InspType, Counter, MaxVal, TimeInterval, RandomFreq, ExpectedTime FROM path_inspections WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var insp = new PathInspection()
            {
              InspectionType = reader.GetString(2),
              Counter = reader.GetString(3),
              MaxVal = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
              TimeInterval = reader.IsDBNull(5) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(5)),
              RandomFreq = reader.IsDBNull(6) ? 0 : reader.GetDouble(6),
              ExpectedInspectionTime = reader.IsDBNull(7) ? null : (TimeSpan?)TimeSpan.FromTicks(reader.GetInt64(7))
            };

            var proc = reader.GetInt32(0);
            var path = reader.GetInt32(1);

            if (path < 1)
            {
              // all paths
              foreach (var pathData in pathDatRows.Values.Where(p => p.Process == proc))
              {
                pathData.Insps.Add(insp);
              }
            }
            else
            {
              // single path
              if (pathDatRows.TryGetValue((proc, path), out var pathData))
              {
                pathData.Insps.Add(insp);
              }
            }
          }
        }

        //hold
        HoldRow jobHold = null;
        cmd.CommandText = "SELECT Process, Path, LoadUnload, UserHold, UserHoldReason, HoldPatternStartUTC, HoldPatternRepeats FROM holds WHERE UniqueStr = $uniq";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            int proc = reader.GetInt32(0);
            int path = reader.GetInt32(1);
            bool load = reader.GetBoolean(2);

            var hold = new HoldRow()
            {
              UserHold = reader.GetBoolean(3),
              ReasonForUserHold = reader.GetString(4),
              HoldUnholdPatternStartUTC = new DateTime(reader.GetInt64(5), DateTimeKind.Utc),
              HoldUnholdPatternRepeats = reader.GetBoolean(6),
            };

            if (proc < 0)
            {
              jobHold = hold;
            }
            else if (load)
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.LoadHold = hold;
              }
            }
            else
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.MachHold = hold;
              }
            }

          }
        }

        //hold pattern
        cmd.CommandText = "SELECT Process, Path, LoadUnload, Span FROM hold_pattern WHERE UniqueStr = $uniq ORDER BY Idx ASC";
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            int proc = reader.GetInt32(0);
            int path = reader.GetInt32(1);
            bool load = reader.GetBoolean(2);
            var time = TimeSpan.FromTicks(reader.GetInt64(3));
            if (proc < 0)
            {
              jobHold?.HoldUnholdPattern.Add(time);
            }
            else if (load)
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.LoadHold?.HoldUnholdPattern.Add(time);
              }
            }
            else
            {
              if (pathDatRows.TryGetValue((proc, path), out var pathRow))
              {
                pathRow.MachHold?.HoldUnholdPattern.Add(time);
              }
            }
          }
        }

        return new JobDetails()
        {
          Hold = jobHold,
          CyclesOnFirstProc = cyclesOnFirstProc.Values.ToImmutableList(),
          Bookings = bookings.ToImmutable(),
          Procs =
            pathDatRows.Values
            .GroupBy(p => p.Process)
            .Select(proc => new ProcessInfo()
            {
              Paths =
                proc
                .OrderBy(p => p.Path)
                .Select(p => new ProcPathInfo()
                {
#pragma warning disable CS0612 // obsolete PathGroup
                  PathGroup = p.PathGroup,
#pragma warning restore CS0612
                  Pallets = p.Pals.ToImmutable(),
                  Fixture = p.Fixture,
                  Face = p.Face,
                  Load = p.Loads.ToImmutable(),
                  ExpectedLoadTime = p.LoadTime,
                  Unload = p.Unloads.ToImmutable(),
                  ExpectedUnloadTime = p.UnloadTime,
                  Stops = p.Stops.Values.Select(s => new MachiningStop()
                  {
                    StationGroup = s.StationGroup,
                    Stations = s.Stations.ToImmutable(),
                    Program = s.Program,
                    ProgramRevision = s.ProgramRevision,
                    Tools = s.Tools.ToImmutable(),
                    ExpectedCycleTime = s.ExpectedCycleTime
                  }).ToImmutableList(),
                  SimulatedProduction = p.SimProd.ToImmutable(),
                  SimulatedStartingUTC = p.StartingUTC,
                  SimulatedAverageFlowTime = p.SimAverageFlowTime,
                  HoldMachining = p.MachHold?.ToHoldPattern(),
                  HoldLoadUnload = p.LoadHold?.ToHoldPattern(),
                  PartsPerPallet = p.PartsPerPallet,
                  InputQueue = p.InputQueue,
                  OutputQueue = p.OutputQueue,
                  Inspections = p.Insps.Count == 0 ? null : p.Insps.ToImmutable(),
                  Casting = p.Casting
                })
                .ToImmutableList()
            })
            .ToImmutableList()
        };
      }
    }

    private ImmutableList<HistoricJob> LoadJobsHelper(IDbCommand cmd, IDbTransaction trans)
    {
      var ret = ImmutableList.CreateBuilder<HistoricJob>();
      using (IDataReader reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          string unique = reader.GetString(0);
          var details = LoadJobData(unique, trans);

          ret.Add(new HistoricJob()
          {
            UniqueStr = unique,
            PartName = reader.GetString(1),
            Comment = reader.IsDBNull(3) ? "" : reader.GetString(3),
            RouteStartUTC = reader.IsDBNull(4) ? DateTime.MinValue : new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
            RouteEndUTC = reader.IsDBNull(5) ? DateTime.MaxValue : new DateTime(reader.GetInt64(5), DateTimeKind.Utc),
            Archived = reader.GetBoolean(6),
            CopiedToSystem = reader.IsDBNull(7) ? false : reader.GetBoolean(7),
            ScheduleId = reader.IsDBNull(8) ? null : reader.GetString(8),
            ManuallyCreated = !reader.IsDBNull(9) && reader.GetBoolean(9),
            AllocationAlgorithm = reader.IsDBNull(10) ? null : reader.GetString(10),
            Cycles = details.CyclesOnFirstProc.Sum(),
            Processes = details.Procs,
            BookingIds = details.Bookings,
            HoldJob = details.Hold?.ToHoldPattern(),
            Decrements = LoadDecrementsForJob(trans, unique)
          });
        }
      }

      return ret.ToImmutable();
    }

    private ImmutableDictionary<string, int> LoadExtraParts(IDbTransaction trans, string schId)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        var ret = ImmutableDictionary.CreateBuilder<string, int>();
        cmd.CommandText = "SELECT Part, Quantity FROM scheduled_parts WHERE ScheduleId == $sid";
        cmd.Parameters.Add("sid", SqliteType.Text).Value = schId;
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            ret.Add(reader.GetString(0), reader.GetInt32(1));
          }
        }
        return ret.ToImmutable();
      }
    }

    private ImmutableList<PartWorkorder> LoadUnfilledWorkorders(IDbTransaction trans, string schId)
    {
      using (var cmd = _connection.CreateCommand())
      using (var prgCmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        ((IDbCommand)prgCmd).Transaction = trans;

        var ret = new Dictionary<(string work, string part), (PartWorkorder work, ImmutableList<WorkorderProgram>.Builder progs)>();
        cmd.CommandText = "SELECT w.Workorder, w.Part, w.Quantity, w.DueDate, w.Priority, p.ProcessNumber, p.StopIndex, p.ProgramName, p.Revision " +
          " FROM unfilled_workorders w " +
          " LEFT OUTER JOIN workorder_programs p ON w.ScheduleId = p.ScheduleId AND w.Workorder = p.Workorder AND w.Part = p.Part " +
          " WHERE w.ScheduleId == $sid AND (Archived IS NULL OR Archived != 1)";
        cmd.Parameters.Add("sid", SqliteType.Integer).Value = schId;
        using (IDataReader reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var workId = reader.GetString(0);
            var part = reader.GetString(1);
            if (!ret.ContainsKey((work: workId, part: part)))
            {
              var workorder = new PartWorkorder()
              {
                WorkorderId = workId,
                Part = part,
                Quantity = reader.GetInt32(2),
                DueDate = new DateTime(reader.GetInt64(3)),
                Priority = reader.GetInt32(4),
                Programs = ImmutableList<WorkorderProgram>.Empty
              };
              ret.Add((work: workId, part: part), (work: workorder, progs: ImmutableList.CreateBuilder<WorkorderProgram>()));
            }

            if (reader.IsDBNull(5)) continue;

            // add the program
            ret[(work: workId, part: part)].progs.Add(new WorkorderProgram()
            {
              ProcessNumber = reader.GetInt32(5),
              StopIndex = reader.IsDBNull(6) ? (int?)null : (int?)reader.GetInt32(6),
              ProgramName = reader.IsDBNull(7) ? null : reader.GetString(7),
              Revision = reader.IsDBNull(8) ? (int?)null : (int?)reader.GetInt32(8)
            });
          }
        }

        return ret.Values.Select(w => w.work with { Programs = w.progs.ToImmutable() }).ToImmutableList();
      }
    }

    private string LatestScheduleId(IDbTransaction trans)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "SELECT MAX(ScheduleId) FROM jobs WHERE ScheduleId IS NOT NULL AND (Manual IS NULL OR Manual == 0)";

        string tag = "";

        object val = cmd.ExecuteScalar();
        if ((val != null))
        {
          tag = val.ToString();
        }

        return tag;
      }
    }

    private ImmutableList<SimulatedStationUtilization> LoadSimulatedStationUse(
        IDbCommand cmd, IDbTransaction trans)
    {
      var ret = ImmutableList.CreateBuilder<SimulatedStationUtilization>();

      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          var sim = new SimulatedStationUtilization()
          {
            ScheduleId = reader.GetString(0),
            StationGroup = reader.GetString(1),
            StationNum = reader.GetInt32(2),
            StartUTC = new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
            EndUTC = new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
            UtilizationTime = TimeSpan.FromTicks(reader.GetInt64(5)),
            PlannedDownTime = reader.IsDBNull(6) ? TimeSpan.Zero : TimeSpan.FromTicks(reader.GetInt64(6))
          };
          ret.Add(sim);
        }
      }

      return ret.ToImmutable();
    }

    private HistoricData LoadHistory(IDbCommand jobCmd, IDbCommand simCmd)
    {
      lock (_cfg)
      {
        var jobs = ImmutableDictionary<string, HistoricJob>.Empty;
        var statUse = ImmutableList<SimulatedStationUtilization>.Empty;

        var trans = _connection.BeginTransaction();
        try
        {
          jobCmd.Transaction = trans;
          simCmd.Transaction = trans;

          jobs = LoadJobsHelper(jobCmd, trans).ToImmutableDictionary(j => j.UniqueStr);
          statUse = LoadSimulatedStationUse(simCmd, trans);

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }

        return new HistoricData()
        {
          Jobs = jobs,
          StationUse = statUse
        };
      }
    }

    // --------------------------------------------------------------------------------
    // Public Loading API
    // --------------------------------------------------------------------------------

    public IReadOnlyList<HistoricJob> LoadUnarchivedJobs()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg FROM jobs WHERE Archived = 0";
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          return LoadJobsHelper(cmd, trans);
        }
      }
    }

    public IReadOnlyList<HistoricJob> LoadJobsNotCopiedToSystem(DateTime startUTC, DateTime endUTC, bool includeDecremented = true)
    {
      var cmdTxt = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg" +
                  " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start AND CopiedToSystem = 0";
      if (!includeDecremented)
      {
        cmdTxt += " AND NOT EXISTS(SELECT 1 FROM job_decrements WHERE job_decrements.JobUnique = jobs.UniqueStr)";
      }
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = cmdTxt;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          return LoadJobsHelper(cmd, trans);
        }
      }
    }

    public HistoricData LoadJobHistory(DateTime startUTC, DateTime endUTC)
    {
      using (var jobCmd = _connection.CreateCommand())
      using (var simCmd = _connection.CreateCommand())
      {
        jobCmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg" +
            " FROM jobs WHERE StartUTC <= $end AND EndUTC >= $start";
        jobCmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        jobCmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;

        simCmd.CommandText = "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime FROM sim_station_use " +
            " WHERE EndUTC >= $start AND StartUTC <= $end";
        simCmd.Parameters.Add("start", SqliteType.Integer).Value = startUTC.Ticks;
        simCmd.Parameters.Add("end", SqliteType.Integer).Value = endUTC.Ticks;

        return LoadHistory(jobCmd, simCmd);
      }
    }

    public HistoricData LoadJobsAfterScheduleId(string schId)
    {
      using (var jobCmd = _connection.CreateCommand())
      using (var simCmd = _connection.CreateCommand())
      {
        jobCmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg" +
            " FROM jobs WHERE ScheduleId > $sid";
        jobCmd.Parameters.Add("sid", SqliteType.Text).Value = schId;

        simCmd.CommandText = "SELECT SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime FROM sim_station_use " +
            " WHERE SimId > $sid";
        simCmd.Parameters.Add("sid", SqliteType.Text).Value = schId;

        return LoadHistory(jobCmd, simCmd);
      }
    }

    public IReadOnlyList<PartWorkorder> MostRecentWorkorders()
    {
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        {
          var sid = LatestScheduleId(trans);
          return LoadUnfilledWorkorders(trans, sid);
        }
      }
    }

    public List<PartWorkorder> MostRecentUnfilledWorkordersForPart(string part)
    {
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        using (var cmd = _connection.CreateCommand())
        {
          cmd.Transaction = trans;

          var sid = LatestScheduleId(trans);

          var ret = new Dictionary<string, (PartWorkorder work, ImmutableList<WorkorderProgram>.Builder progs)>();
          cmd.CommandText = "SELECT w.Workorder, w.Quantity, w.DueDate, w.Priority, p.ProcessNumber, p.StopIndex, p.ProgramName, p.Revision" +
            " FROM unfilled_workorders w " +
            " LEFT OUTER JOIN workorder_programs p ON w.ScheduleId = p.ScheduleId AND w.Workorder = p.Workorder AND w.Part = p.Part " +
            " WHERE w.ScheduleId = $sid AND w.Part = $part AND (Archived IS NULL OR Archived != 1)";
          cmd.Parameters.Add("sid", SqliteType.Text).Value = sid;
          cmd.Parameters.Add("part", SqliteType.Text).Value = part;

          using (IDataReader reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var workId = reader.GetString(0);
              if (!ret.ContainsKey(workId))
              {
                var workorder = new PartWorkorder()
                {
                  WorkorderId = workId,
                  Part = part,
                  Quantity = reader.GetInt32(1),
                  DueDate = new DateTime(reader.GetInt64(2)),
                  Priority = reader.GetInt32(3)
                };
                ret.Add(workId, (work: workorder, progs: ImmutableList.CreateBuilder<WorkorderProgram>()));
              }

              if (reader.IsDBNull(4)) continue;

              // add the program
              ret[workId].progs.Add(new WorkorderProgram()
              {
                ProcessNumber = reader.GetInt32(4),
                StopIndex = reader.IsDBNull(5) ? (int?)null : (int?)reader.GetInt32(5),
                ProgramName = reader.IsDBNull(6) ? null : reader.GetString(6),
                Revision = reader.IsDBNull(7) ? (int?)null : (int?)reader.GetInt32(7)
              });
            }
          }

          trans.Commit();
          return ret.Values.Select(w => w.work with { Programs = w.progs.ToImmutable() }).ToList();
        }
      }
    }

    public List<PartWorkorder> WorkordersById(string workorderId)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          var ret = new Dictionary<string, (PartWorkorder work, ImmutableList<WorkorderProgram>.Builder progs)>();
          cmd.CommandText = "SELECT w.Part, w.Quantity, w.DueDate, w.Priority, p.ProcessNumber, p.StopIndex, p.ProgramName, p.Revision" +
            " FROM unfilled_workorders w " +
            " LEFT OUTER JOIN workorder_programs p ON w.ScheduleId = p.ScheduleId AND w.Workorder = p.Workorder AND w.Part = p.Part " +
            " WHERE " +
            "    w.ScheduleId = (SELECT MAX(v.ScheduleId) FROM unfilled_workorders v WHERE v.Workorder = $work)" +
            "    AND w.Workorder = $work";
          cmd.Parameters.Add("work", SqliteType.Text).Value = workorderId;

          using (IDataReader reader = cmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var part = reader.GetString(0);

              if (!ret.ContainsKey(part))
              {
                var workorder = new PartWorkorder()
                {
                  WorkorderId = workorderId,
                  Part = part,
                  Quantity = reader.GetInt32(1),
                  DueDate = new DateTime(reader.GetInt64(2)),
                  Priority = reader.GetInt32(3)
                };
                ret.Add(part, (work: workorder, progs: ImmutableList.CreateBuilder<WorkorderProgram>()));
              }

              if (reader.IsDBNull(4)) continue;

              // add the program
              ret[part].progs.Add(new WorkorderProgram()
              {
                ProcessNumber = reader.GetInt32(4),
                StopIndex = reader.IsDBNull(5) ? (int?)null : (int?)reader.GetInt32(5),
                ProgramName = reader.IsDBNull(6) ? null : reader.GetString(6),
                Revision = reader.IsDBNull(7) ? (int?)null : (int?)reader.GetInt32(7)
              });
            }
          }

          return ret.Values.Select(w => w.work with { Programs = w.progs.ToImmutable() }).ToList();
        }
      }
    }

    public PlannedSchedule LoadMostRecentSchedule()
    {
      using (var cmd = _connection.CreateCommand())
      {
        cmd.CommandText = "SELECT UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg" +
                  " FROM jobs WHERE ScheduleId = $sid";

        lock (_cfg)
        {
          using (var trans = _connection.BeginTransaction())
          {
            var latestSchId = LatestScheduleId(trans);
            cmd.Parameters.Add("sid", SqliteType.Text).Value = latestSchId;
            cmd.Transaction = trans;
            return new PlannedSchedule()
            {
              LatestScheduleId = latestSchId,
              Jobs = LoadJobsHelper(cmd, trans),
              ExtraParts = LoadExtraParts(trans, latestSchId),
              CurrentUnfilledWorkorders = LoadUnfilledWorkorders(trans, latestSchId),
            };
          }
        }
      }
    }

    public HistoricJob LoadJob(string UniqueStr)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {

          HistoricJob job = null;

          cmd.CommandText = "SELECT Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg FROM jobs WHERE UniqueStr = $uniq";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;

          var trans = _connection.BeginTransaction();
          try
          {
            cmd.Transaction = trans;

            using (IDataReader reader = cmd.ExecuteReader())
            {
              if (reader.Read())
              {

                var details = LoadJobData(UniqueStr, trans);

                job = new HistoricJob()
                {
                  UniqueStr = UniqueStr,
                  PartName = reader.GetString(0),
                  Comment = reader.IsDBNull(2) ? "" : reader.GetString(2),
                  RouteStartUTC = reader.IsDBNull(3) ? DateTime.MinValue : new DateTime(reader.GetInt64(3), DateTimeKind.Utc),
                  RouteEndUTC = reader.IsDBNull(4) ? DateTime.MaxValue : new DateTime(reader.GetInt64(4), DateTimeKind.Utc),
                  Archived = reader.GetBoolean(5),
                  CopiedToSystem = reader.IsDBNull(6) ? false : reader.GetBoolean(6),
                  ScheduleId = reader.IsDBNull(7) ? null : reader.GetString(7),
                  ManuallyCreated = !reader.IsDBNull(8) && reader.GetBoolean(8),
                  AllocationAlgorithm = reader.IsDBNull(9) ? null : reader.GetString(9),
                  Cycles = details.CyclesOnFirstProc.Sum(),
                  Processes = details.Procs,
                  BookingIds = details.Bookings,
                  HoldJob = details.Hold?.ToHoldPattern(),
                  Decrements = LoadDecrementsForJob(trans, UniqueStr)
                };
              }
            }

            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }

          return job;
        }
      }
    }

    public bool DoesJobExist(string unique)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT COUNT(*) FROM jobs WHERE UniqueStr = $uniq";
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;

          object cnt = cmd.ExecuteScalar();
          if (cnt != null & Convert.ToInt32(cnt) > 0)
            return true;
          else
            return false;
        }
      }
    }

    #endregion

    #region "Adding and deleting"

    public void AddJobs(NewJobs newJobs, string expectedPreviousScheduleId, bool addAsCopiedToSystem)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          if (!string.IsNullOrEmpty(expectedPreviousScheduleId))
          {
            var last = LatestScheduleId(trans);
            if (last != expectedPreviousScheduleId)
            {
              throw new BadRequestException(string.Format("Mismatch in previous schedule: expected '{0}' but got '{1}'", expectedPreviousScheduleId, last));
            }
          }

          // add programs first so that the lookup of latest program revision will use newest programs
          var startingUtc = DateTime.UtcNow;
          if (newJobs.Jobs.Any())
          {
            startingUtc = newJobs.Jobs[0].RouteStartUTC;
          }

          var negRevisionMap = AddPrograms(trans, newJobs.Programs, startingUtc);

          foreach (var job in newJobs.Jobs)
          {
            AddJob(trans, job, negRevisionMap, addAsCopiedToSystem, newJobs.ScheduleId);
          }

          AddSimulatedStations(trans, newJobs.StationUse);

          if (!string.IsNullOrEmpty(newJobs.ScheduleId) && newJobs.ExtraParts != null)
          {
            AddExtraParts(trans, newJobs.ScheduleId, newJobs.ExtraParts);
          }

          if (!string.IsNullOrEmpty(newJobs.ScheduleId) && newJobs.CurrentUnfilledWorkorders != null)
          {
            AddUnfilledWorkorders(trans, newJobs.ScheduleId, newJobs.CurrentUnfilledWorkorders, negRevisionMap);
          }

          if (!string.IsNullOrEmpty(newJobs.ScheduleId) && newJobs.DebugMessage != null)
          {
            using (var cmd = _connection.CreateCommand())
            {
              cmd.Transaction = trans;
              cmd.CommandText = "INSERT OR REPLACE INTO schedule_debug(ScheduleId, DebugMessage) VALUES ($sid,$debug)";
              cmd.Parameters.Add("sid", SqliteType.Text).Value = newJobs.ScheduleId;
              cmd.Parameters.Add("debug", SqliteType.Blob).Value = newJobs.DebugMessage;
              cmd.ExecuteNonQuery();
            }
          }

          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void AddPrograms(IEnumerable<ProgramEntry> programs, DateTime startingUtc)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          AddPrograms(trans, programs, startingUtc);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void AddJob(IDbTransaction trans, Job job, Dictionary<(string prog, long rev), long> negativeRevisionMap, bool addAsCopiedToSystem, string schId)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText =
          "INSERT INTO jobs(UniqueStr, Part, NumProcess, Comment, StartUTC, EndUTC, Archived, CopiedToSystem, ScheduleId, Manual, AllocateAlg) " +
            "VALUES($uniq,$part,$proc,$comment,$start,$end,$archived,$copied,$sid,$manual,$alg)";

        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("part", SqliteType.Text).Value = job.PartName;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = job.Processes.Count;
        if (string.IsNullOrEmpty(job.Comment))
          cmd.Parameters.Add("comment", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("comment", SqliteType.Text).Value = job.Comment;
        cmd.Parameters.Add("start", SqliteType.Integer).Value = job.RouteStartUTC.Ticks;
        cmd.Parameters.Add("end", SqliteType.Integer).Value = job.RouteEndUTC.Ticks;
        cmd.Parameters.Add("archived", SqliteType.Integer).Value = job.Archived;
        cmd.Parameters.Add("copied", SqliteType.Integer).Value = addAsCopiedToSystem;
        if (string.IsNullOrEmpty(schId))
          cmd.Parameters.Add("sid", SqliteType.Text).Value = DBNull.Value;
        else
          cmd.Parameters.Add("sid", SqliteType.Text).Value = schId;
        cmd.Parameters.Add("manual", SqliteType.Integer).Value = job.ManuallyCreated;
        cmd.Parameters.Add("alg", SqliteType.Text).Value = string.IsNullOrEmpty(job.AllocationAlgorithm) ? DBNull.Value : job.AllocationAlgorithm;

        cmd.ExecuteNonQuery();

        if (job.BookingIds != null)
        {
          cmd.CommandText = "INSERT INTO scheduled_bookings(UniqueStr, BookingId) VALUES ($uniq,$booking)";
          cmd.Parameters.Clear();
          cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
          cmd.Parameters.Add("booking", SqliteType.Text);
          foreach (var b in job.BookingIds)
          {
            cmd.Parameters[1].Value = b;
            cmd.ExecuteNonQuery();
          }
        }

        // eventually move to store directly on job table, but leave here for backwards compatibility
        cmd.CommandText = "INSERT INTO planqty(UniqueStr, Path, PlanQty) VALUES ($uniq,$path,$plan)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = 0;
        cmd.Parameters.Add("plan", SqliteType.Integer).Value = job.Cycles;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT OR REPLACE INTO simulated_production(UniqueStr, Process, Path, TimeUTC, Quantity) VALUES ($uniq,$proc,$path,$time,$qty)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("time", SqliteType.Integer);
        cmd.Parameters.Add("qty", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            if (path.SimulatedProduction == null) continue;
            foreach (var prod in path.SimulatedProduction)
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = prod.TimeUTC.Ticks;
              cmd.Parameters[4].Value = prod.Quantity;
              cmd.ExecuteNonQuery();
            }
          }
        }

        cmd.CommandText = "INSERT INTO pallets(UniqueStr, Process, Path, Pallet) VALUES ($uniq,$proc,$path,$pal)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("pal", SqliteType.Text);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            foreach (string pal in path.Pallets)
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = pal;
              cmd.ExecuteNonQuery();
            }
          }
        }

        cmd.CommandText = "INSERT INTO pathdata(UniqueStr, Process, Path, StartingUTC, PartsPerPallet, PathGroup,SimAverageFlowTime,InputQueue,OutputQueue,LoadTime,UnloadTime,Fixture,Face,Casting) " +
      "VALUES ($uniq,$proc,$path,$start,$ppp,$group,$flow,$iq,$oq,$lt,$ul,$fix,$face,$casting)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("start", SqliteType.Integer);
        cmd.Parameters.Add("ppp", SqliteType.Integer);
        cmd.Parameters.Add("group", SqliteType.Integer);
        cmd.Parameters.Add("flow", SqliteType.Integer);
        cmd.Parameters.Add("iq", SqliteType.Text);
        cmd.Parameters.Add("oq", SqliteType.Text);
        cmd.Parameters.Add("lt", SqliteType.Integer);
        cmd.Parameters.Add("ul", SqliteType.Integer);
        cmd.Parameters.Add("fix", SqliteType.Text);
        cmd.Parameters.Add("face", SqliteType.Integer);
        cmd.Parameters.Add("casting", SqliteType.Text);
        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            cmd.Parameters[1].Value = i;
            cmd.Parameters[2].Value = j;
            cmd.Parameters[3].Value = path.SimulatedStartingUTC.Ticks;
            cmd.Parameters[4].Value = path.PartsPerPallet;
#pragma warning disable CS0612 // obsolete PathGroup
            cmd.Parameters[5].Value = path.PathGroup;
#pragma warning restore CS0612
            cmd.Parameters[6].Value = path.SimulatedAverageFlowTime.Ticks;
            var iq = path.InputQueue;
            if (string.IsNullOrEmpty(iq))
              cmd.Parameters[7].Value = DBNull.Value;
            else
              cmd.Parameters[7].Value = iq;
            var oq = path.OutputQueue;
            if (string.IsNullOrEmpty(oq))
              cmd.Parameters[8].Value = DBNull.Value;
            else
              cmd.Parameters[8].Value = oq;
            cmd.Parameters[9].Value = path.ExpectedLoadTime.Ticks;
            cmd.Parameters[10].Value = path.ExpectedUnloadTime.Ticks;
            var (fix, face) = (path.Fixture, path.Face);
            cmd.Parameters[11].Value = string.IsNullOrEmpty(fix) ? DBNull.Value : (object)fix;
            cmd.Parameters[12].Value = face ?? 0;
            if (i == 1)
            {
              var casting = path.Casting;
              cmd.Parameters[13].Value = string.IsNullOrEmpty(casting) ? DBNull.Value : (object)casting;
            }
            else
            {
              cmd.Parameters[13].Value = DBNull.Value;
            }
            cmd.ExecuteNonQuery();
          }
        }

        cmd.CommandText = "INSERT INTO stops(UniqueStr, Process, Path, RouteNum, StatGroup, ExpectedCycleTime, Program, ProgramRevision) " +
      "VALUES ($uniq,$proc,$path,$route,$group,$cycle,$prog,$rev)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("group", SqliteType.Text);
        cmd.Parameters.Add("cycle", SqliteType.Integer);
        cmd.Parameters.Add("prog", SqliteType.Text);
        cmd.Parameters.Add("rev", SqliteType.Integer);


        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            int routeNum = 0;
            foreach (var entry in path.Stops)
            {
              long? rev = null;
              if (!entry.ProgramRevision.HasValue || entry.ProgramRevision.Value == 0)
              {
                if (!string.IsNullOrEmpty(entry.Program))
                {
                  rev = LatestRevisionForProgram(trans, entry.Program);
                }
              }
              else if (entry.ProgramRevision.Value > 0)
              {
                rev = entry.ProgramRevision.Value;
              }
              else if (negativeRevisionMap.TryGetValue((prog: entry.Program, rev: entry.ProgramRevision.Value), out long convertedRev))
              {
                rev = convertedRev;
              }
              else
              {
                throw new BadRequestException($"Part {job.PartName}, process {i}, path {j}, stop {routeNum}, " +
                  "has a negative program revision but no matching negative program revision exists in the downloaded ProgramEntry list");
              }
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;
              cmd.Parameters[3].Value = routeNum;
              cmd.Parameters[4].Value = entry.StationGroup;
              cmd.Parameters[5].Value = entry.ExpectedCycleTime.Ticks;
              cmd.Parameters[6].Value = string.IsNullOrEmpty(entry.Program) ? DBNull.Value : (object)entry.Program;
              cmd.Parameters[7].Value = rev != null ? (object)rev : DBNull.Value;
              cmd.ExecuteNonQuery();
              routeNum += 1;
            }
          }
        }

        cmd.CommandText = "INSERT INTO stops_stations(UniqueStr, Process, Path, RouteNum, StatNum) " +
      "VALUES ($uniq,$proc,$path,$route,$num)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("num", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            int routeNum = 0;
            foreach (var entry in path.Stops)
            {
              foreach (var stat in entry.Stations)
              {
                cmd.Parameters[1].Value = i;
                cmd.Parameters[2].Value = j;
                cmd.Parameters[3].Value = routeNum;
                cmd.Parameters[4].Value = stat;

                cmd.ExecuteNonQuery();
              }
              routeNum += 1;
            }
          }
        }

        cmd.CommandText = "INSERT INTO tools(UniqueStr, Process, Path, RouteNum, Tool, ExpectedUse) " +
      "VALUES ($uniq,$proc,$path,$route,$tool,$use)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("route", SqliteType.Integer);
        cmd.Parameters.Add("tool", SqliteType.Text);
        cmd.Parameters.Add("use", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            int routeNum = 0;
            foreach (var entry in path.Stops)
            {
              foreach (var tool in entry.Tools)
              {
                cmd.Parameters[1].Value = i;
                cmd.Parameters[2].Value = j;
                cmd.Parameters[3].Value = routeNum;
                cmd.Parameters[4].Value = tool.Key;
                cmd.Parameters[5].Value = tool.Value.Ticks;
                cmd.ExecuteNonQuery();
              }
              routeNum += 1;
            }
          }
        }

        cmd.CommandText = "INSERT INTO loadunload(UniqueStr,Process,Path,StatNum,Load) VALUES ($uniq,$proc,$path,$stat,$load)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("stat", SqliteType.Integer);
        cmd.Parameters.Add("load", SqliteType.Integer);

        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            cmd.Parameters[1].Value = i;
            cmd.Parameters[2].Value = j;
            cmd.Parameters[4].Value = true;
            foreach (int statNum in path.Load)
            {
              cmd.Parameters[3].Value = statNum;
              cmd.ExecuteNonQuery();
            }
            cmd.Parameters[4].Value = false;
            foreach (int statNum in path.Unload)
            {
              cmd.Parameters[3].Value = statNum;
              cmd.ExecuteNonQuery();
            }
          }
        }


        cmd.CommandText = "INSERT INTO path_inspections(UniqueStr,Process,Path,InspType,Counter,MaxVal,TimeInterval,RandomFreq,ExpectedTime) "
          + "VALUES ($uniq,$proc,$path,$insp,$cnt,$max,$time,$freq,$expected)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = job.UniqueStr;
        cmd.Parameters.Add("proc", SqliteType.Integer);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("insp", SqliteType.Text);
        cmd.Parameters.Add("cnt", SqliteType.Text);
        cmd.Parameters.Add("max", SqliteType.Integer);
        cmd.Parameters.Add("time", SqliteType.Integer);
        cmd.Parameters.Add("freq", SqliteType.Real);
        cmd.Parameters.Add("expected", SqliteType.Integer);
        for (int i = 1; i <= job.Processes.Count; i++)
        {
          var proc = job.Processes[i - 1];
          for (int j = 1; j <= proc.Paths.Count; j++)
          {
            var path = proc.Paths[j - 1];
            if (path.Inspections != null)
            {
              cmd.Parameters[1].Value = i;
              cmd.Parameters[2].Value = j;

              foreach (var insp in path.Inspections)
              {
                cmd.Parameters[3].Value = insp.InspectionType;
                cmd.Parameters[4].Value = insp.Counter;
                cmd.Parameters[5].Value = insp.MaxVal > 0 ? (object)insp.MaxVal : DBNull.Value;
                cmd.Parameters[6].Value = insp.TimeInterval.Ticks > 0 ? (object)insp.TimeInterval.Ticks : DBNull.Value;
                cmd.Parameters[7].Value = insp.RandomFreq > 0 ? (object)insp.RandomFreq : DBNull.Value;
                cmd.Parameters[8].Value = insp.ExpectedInspectionTime.HasValue && insp.ExpectedInspectionTime.Value.Ticks > 0 ?
                  (object)insp.ExpectedInspectionTime.Value.Ticks : DBNull.Value;
                cmd.ExecuteNonQuery();
              }
            }
          }
        }
      }

      InsertHold(job.UniqueStr, -1, -1, false, job.HoldJob, trans);
      for (int i = 1; i <= job.Processes.Count; i++)
      {
        var proc = job.Processes[i - 1];
        for (int j = 1; j <= proc.Paths.Count; j++)
        {
          var path = proc.Paths[j - 1];
          InsertHold(job.UniqueStr, i, j, true, path.HoldLoadUnload, trans);
          InsertHold(job.UniqueStr, i, j, false, path.HoldMachining, trans);
        }
      }
    }

    private void AddSimulatedStations(IDbTransaction trans, IEnumerable<SimulatedStationUtilization> simStats)
    {
      if (simStats == null) return;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT OR REPLACE INTO sim_station_use(SimId, StationGroup, StationNum, StartUTC, EndUTC, UtilizationTime, PlanDownTime) " +
            " VALUES($simid,$group,$num,$start,$end,$utilization,$plandown)";
        cmd.Parameters.Add("simid", SqliteType.Text);
        cmd.Parameters.Add("group", SqliteType.Text);
        cmd.Parameters.Add("num", SqliteType.Integer);
        cmd.Parameters.Add("start", SqliteType.Integer);
        cmd.Parameters.Add("end", SqliteType.Integer);
        cmd.Parameters.Add("utilization", SqliteType.Integer);
        cmd.Parameters.Add("plandown", SqliteType.Integer);

        foreach (var sim in simStats)
        {
          cmd.Parameters[0].Value = sim.ScheduleId;
          cmd.Parameters[1].Value = sim.StationGroup;
          cmd.Parameters[2].Value = sim.StationNum;
          cmd.Parameters[3].Value = sim.StartUTC.Ticks;
          cmd.Parameters[4].Value = sim.EndUTC.Ticks;
          cmd.Parameters[5].Value = sim.UtilizationTime.Ticks;
          cmd.Parameters[6].Value = sim.PlannedDownTime.Ticks;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddExtraParts(IDbTransaction trans, string scheduleId, IDictionary<string, int> extraParts)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;


        cmd.CommandText = "INSERT OR REPLACE INTO scheduled_parts(ScheduleId, Part, Quantity) VALUES ($sid,$part,$qty)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);
        foreach (var p in extraParts)
        {
          cmd.Parameters[1].Value = p.Key;
          cmd.Parameters[2].Value = p.Value;
          cmd.ExecuteNonQuery();
        }
      }
    }

    private void AddUnfilledWorkorders(IDbTransaction trans, string scheduleId, IEnumerable<PartWorkorder> workorders, Dictionary<(string prog, long rev), long> negativeRevisionMap)
    {
      using (var cmd = _connection.CreateCommand())
      using (var prgCmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        ((IDbCommand)prgCmd).Transaction = trans;


        cmd.CommandText = "INSERT OR REPLACE INTO unfilled_workorders(ScheduleId, Workorder, Part, Quantity, DueDate, Priority, Archived) VALUES ($sid,$work,$part,$qty,$due,$pri,NULL)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        cmd.Parameters.Add("work", SqliteType.Text);
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);
        cmd.Parameters.Add("due", SqliteType.Integer);
        cmd.Parameters.Add("pri", SqliteType.Integer);

        prgCmd.CommandText = "INSERT OR REPLACE INTO workorder_programs(ScheduleId, Workorder, Part, ProcessNumber, StopIndex, ProgramName, Revision) VALUES ($sid,$work,$part,$proc,$stop,$name,$rev)";
        prgCmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
        prgCmd.Parameters.Add("work", SqliteType.Text);
        prgCmd.Parameters.Add("part", SqliteType.Text);
        prgCmd.Parameters.Add("proc", SqliteType.Integer);
        prgCmd.Parameters.Add("stop", SqliteType.Integer);
        prgCmd.Parameters.Add("name", SqliteType.Text);
        prgCmd.Parameters.Add("rev", SqliteType.Integer);

        foreach (var w in workorders)
        {
          cmd.Parameters[1].Value = w.WorkorderId;
          cmd.Parameters[2].Value = w.Part;
          cmd.Parameters[3].Value = w.Quantity;
          cmd.Parameters[4].Value = w.DueDate.Ticks;
          cmd.Parameters[5].Value = w.Priority;
          cmd.ExecuteNonQuery();

          if (w.Programs != null)
          {
            foreach (var prog in w.Programs)
            {
              long? rev = null;
              if (!prog.Revision.HasValue || prog.Revision.Value == 0)
              {
                if (!string.IsNullOrEmpty(prog.ProgramName))
                {
                  rev = LatestRevisionForProgram(trans, prog.ProgramName);
                }
              }
              else if (prog.Revision.Value > 0)
              {
                rev = prog.Revision.Value;
              }
              else if (negativeRevisionMap.TryGetValue((prog: prog.ProgramName, rev: prog.Revision.Value), out long convertedRev))
              {
                rev = convertedRev;
              }
              else
              {
                throw new BadRequestException($"Workorder {w.WorkorderId} " +
                  "has a negative program revision but no matching negative program revision exists in the downloaded ProgramEntry list");
              }

              prgCmd.Parameters[1].Value = w.WorkorderId;
              prgCmd.Parameters[2].Value = w.Part;
              prgCmd.Parameters[3].Value = prog.ProcessNumber;
              prgCmd.Parameters[4].Value = prog.StopIndex.HasValue ? (object)prog.StopIndex.Value : DBNull.Value;
              prgCmd.Parameters[5].Value = prog.ProgramName;
              prgCmd.Parameters[6].Value = rev != null ? (object)rev : DBNull.Value;
              prgCmd.ExecuteNonQuery();
            }
          }
        }
      }
    }

    private void InsertHold(string unique, int proc, int path, bool load, HoldPattern newHold,
                            IDbTransaction trans)
    {
      if (newHold == null) return;

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO holds(UniqueStr,Process,Path,LoadUnload,UserHold,UserHoldReason,HoldPatternStartUTC,HoldPatternRepeats) " +
      "VALUES ($uniq,$proc,$path,$load,$hold,$holdR,$holdT,$holdP)";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
        cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
        cmd.Parameters.Add("hold", SqliteType.Integer).Value = newHold.UserHold;
        cmd.Parameters.Add("holdR", SqliteType.Text).Value = newHold.ReasonForUserHold;
        cmd.Parameters.Add("holdT", SqliteType.Integer).Value = newHold.HoldUnholdPatternStartUTC.Ticks;
        cmd.Parameters.Add("holdP", SqliteType.Integer).Value = newHold.HoldUnholdPatternRepeats;
        cmd.ExecuteNonQuery();

        cmd.CommandText = "INSERT INTO hold_pattern(UniqueStr,Process,Path,LoadUnload,Idx,Span) " +
      "VALUES ($uniq,$proc,$path,$stat,$idx,$span)";
        cmd.Parameters.Clear();
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
        cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
        cmd.Parameters.Add("stat", SqliteType.Integer).Value = load;
        cmd.Parameters.Add("idx", SqliteType.Integer);
        cmd.Parameters.Add("span", SqliteType.Integer);
        for (int i = 0; i < newHold.HoldUnholdPattern.Count; i++)
        {
          cmd.Parameters[4].Value = i;
          cmd.Parameters[5].Value = newHold.HoldUnholdPattern[i].Ticks;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void ArchiveJob(string UniqueStr)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, new[] { UniqueStr }, archived: true);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void ArchiveJobs(IEnumerable<string> uniqueStrs, IEnumerable<NewDecrementQuantity> newDecrements = null, DateTime? nowUTC = null)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, uniqueStrs, archived: true);
          if (newDecrements != null)
          {
            AddNewDecrement(
              trans: trans,
              counts: newDecrements,
              removedBookings: null,
              nowUTC: nowUTC);
          }
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void UnarchiveJob(string UniqueStr)
    {
      UnarchiveJobs(new[] { UniqueStr });
    }

    public void UnarchiveJobs(IEnumerable<string> uniqueStrs, DateTime? nowUTC = null)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          SetArchived(trans, uniqueStrs, archived: false);
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    private void SetArchived(IDbTransaction trans, IEnumerable<string> uniqs, bool archived)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "UPDATE jobs SET Archived = $archived WHERE UniqueStr = $uniq";
        var param = cmd.Parameters.Add("uniq", SqliteType.Text);
        cmd.Parameters.Add("archived", SqliteType.Integer).Value = archived ? 1 : 0;
        foreach (var uniqStr in uniqs)
        {
          param.Value = uniqStr;
          cmd.ExecuteNonQuery();
        }
      }
    }

    public void MarkJobCopiedToSystem(string UniqueStr)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          {
            cmd.CommandText = "UPDATE jobs SET CopiedToSystem = 1 WHERE UniqueStr = $uniq";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = UniqueStr;
            ((IDbCommand)cmd).Transaction = trans;
            cmd.ExecuteNonQuery();
            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }
    #endregion

    #region "Modification of Jobs"
    public void SetJobComment(string unique, string comment)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {

          var trans = _connection.BeginTransaction();

          try
          {
            cmd.Transaction = trans;

            cmd.CommandText = "UPDATE jobs SET Comment = $comment WHERE UniqueStr = $uniq";
            cmd.Parameters.Add("comment", SqliteType.Text).Value = comment;
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.ExecuteNonQuery();
            trans.Commit();
          }
          catch
          {
            trans.Rollback();
            throw;
          }
        }
      }
    }
    public void UpdateJobHold(string unique, HoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, -1, -1, false, newHold);
    }

    public void UpdateJobMachiningHold(string unique, int proc, int path, HoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, proc, path, false, newHold);
    }

    public void UpdateJobLoadUnloadHold(string unique, int proc, int path, HoldPattern newHold)
    {
      UpdateJobHoldHelper(unique, proc, path, true, newHold);
    }

    private void UpdateJobHoldHelper(string unique, int proc, int path, bool load, HoldPattern newHold)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();

        try
        {

          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;

            cmd.CommandText = "DELETE FROM holds WHERE UniqueStr = $uniq AND Process = $proc AND Path = $path AND LoadUnload = $load";
            cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
            cmd.Parameters.Add("proc", SqliteType.Integer).Value = proc;
            cmd.Parameters.Add("path", SqliteType.Integer).Value = path;
            cmd.Parameters.Add("load", SqliteType.Integer).Value = load;
            cmd.ExecuteNonQuery();

            cmd.CommandText = "DELETE FROM hold_pattern WHERE UniqueStr = $uniq AND Process = $proc AND Path = $path AND LoadUnload = $load";
            cmd.ExecuteNonQuery();

            InsertHold(unique, proc, path, load, newHold, trans);

            trans.Commit();
          }
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public void ReplaceWorkordersForSchedule(string scheduleId, IEnumerable<PartWorkorder> newWorkorders, IEnumerable<ProgramEntry> programs, DateTime? nowUtc = null)
    {
      lock (_cfg)
      {
        using (var trans = _connection.BeginTransaction())
        using (var getWorksCmd = _connection.CreateCommand())
        using (var archiveCmd = _connection.CreateCommand())
        using (var delProgsCmd = _connection.CreateCommand())
        {
          getWorksCmd.Transaction = trans;
          archiveCmd.Transaction = trans;
          delProgsCmd.Transaction = trans;

          getWorksCmd.CommandText = "SELECT Workorder, Part FROM unfilled_workorders WHERE ScheduleId = $sid AND (Archived IS NULL OR Archived != 1)";
          getWorksCmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;

          archiveCmd.CommandText = "UPDATE unfilled_workorders SET Archived = 1 WHERE ScheduleId = $sid AND Workorder = $work AND Part = $part";
          archiveCmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
          var archiveWorkorder = archiveCmd.Parameters.Add("work", SqliteType.Text);
          var archivePart = archiveCmd.Parameters.Add("part", SqliteType.Text);

          delProgsCmd.CommandText = "DELETE FROM workorder_programs WHERE ScheduleId = $sid AND Workorder = $work AND Part = $part";
          delProgsCmd.Parameters.Add("sid", SqliteType.Text).Value = scheduleId;
          var delWorkorder = delProgsCmd.Parameters.Add("work", SqliteType.Text);
          var delPart = delProgsCmd.Parameters.Add("part", SqliteType.Text);

          var newWorkorderIds = new HashSet<(string work, string part)>(
            newWorkorders.Select(w => (work: w.WorkorderId, part: w.Part))
          );

          using (var reader = getWorksCmd.ExecuteReader())
          {
            while (reader.Read())
            {
              var work = reader.GetString(0);
              var part = reader.GetString(1);

              if (newWorkorderIds.Contains((work: work, part: part)))
              {
                // will be replaced below, for now clear out programs
                delWorkorder.Value = work;
                delPart.Value = part;
                delProgsCmd.ExecuteNonQuery();
              }
              else
              {
                // missing from new, archive
                archiveWorkorder.Value = work;
                archivePart.Value = part;
                archiveCmd.ExecuteNonQuery();
              }
            }
          }

          var negProgMap = AddPrograms(trans, programs, nowUtc ?? DateTime.UtcNow);
          AddUnfilledWorkorders(trans, scheduleId, newWorkorders, negProgMap);

          trans.Commit();
        }
      }
    }
    #endregion

    #region Decrement Counts
    public void AddNewDecrement(IEnumerable<NewDecrementQuantity> counts, DateTime? nowUTC = null, IEnumerable<RemovedBooking> removedBookings = null)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          AddNewDecrement(
            trans: trans,
            counts: counts,
            removedBookings: removedBookings,
            nowUTC: nowUTC
          );
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }

    }

    private void AddNewDecrement(IDbTransaction trans, IEnumerable<NewDecrementQuantity> counts, IEnumerable<RemovedBooking> removedBookings, DateTime? nowUTC)
    {
      var now = nowUTC ?? DateTime.UtcNow;
      long decrementId = 0;
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT MAX(DecrementId) FROM job_decrements";
        var lastDecId = cmd.ExecuteScalar();
        if (lastDecId != null && lastDecId != DBNull.Value)
        {
          decrementId = Convert.ToInt64(lastDecId) + 1;
        }
      }

      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;

        cmd.CommandText = "INSERT INTO job_decrements(DecrementId,JobUnique,Proc1Path,TimeUTC,Part,Quantity) VALUES ($id,$uniq,$path,$now,$part,$qty)";
        cmd.Parameters.Add("id", SqliteType.Integer);
        cmd.Parameters.Add("uniq", SqliteType.Text);
        cmd.Parameters.Add("path", SqliteType.Integer);
        cmd.Parameters.Add("now", SqliteType.Integer);
        cmd.Parameters.Add("part", SqliteType.Text);
        cmd.Parameters.Add("qty", SqliteType.Integer);

        foreach (var q in counts)
        {
          cmd.Parameters[0].Value = decrementId;
          cmd.Parameters[1].Value = q.JobUnique;
          cmd.Parameters[2].Value = 1; // For now, leave Proc1Path in the database
          cmd.Parameters[3].Value = now.Ticks;
          cmd.Parameters[4].Value = q.Part;
          cmd.Parameters[5].Value = q.Quantity;
          cmd.ExecuteNonQuery();
        }
      }

      if (removedBookings != null)
      {
        using (var cmd = _connection.CreateCommand())
        {
          ((IDbCommand)cmd).Transaction = trans;

          cmd.CommandText = "DELETE FROM scheduled_bookings WHERE UniqueStr = $u AND BookingId = $b";
          cmd.Parameters.Add("u", SqliteType.Text);
          cmd.Parameters.Add("b", SqliteType.Text);

          foreach (var b in removedBookings)
          {
            cmd.Parameters[0].Value = b.JobUnique;
            cmd.Parameters[1].Value = b.BookingId;
            cmd.ExecuteNonQuery();
          }
        }
      }
    }

    public ImmutableList<DecrementQuantity> LoadDecrementsForJob(string unique)
    {
      lock (_cfg)
      {
        return LoadDecrementsForJob(trans: null, unique: unique);
      }
    }

    private ImmutableList<DecrementQuantity> LoadDecrementsForJob(IDbTransaction trans, string unique)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT DecrementId,TimeUTC,Quantity FROM job_decrements WHERE JobUnique = $uniq";
        cmd.Parameters.Add("uniq", SqliteType.Text).Value = unique;
        var ret = ImmutableList.CreateBuilder<DecrementQuantity>();
        using (var reader = cmd.ExecuteReader())
        {
          while (reader.Read())
          {
            var j = new DecrementQuantity()
            {
              DecrementId = reader.GetInt64(0),
              TimeUTC = new DateTime(reader.GetInt64(1), DateTimeKind.Utc),
              Quantity = reader.GetInt32(2),
            };
            ret.Add(j);
          }
          return ret.ToImmutable();
        }
      }
    }

    public List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(long afterId)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM job_decrements WHERE DecrementId > $after";
          cmd.Parameters.Add("after", SqliteType.Integer).Value = afterId;
          return LoadDecrementQuantitiesHelper(cmd);
        }
      }
    }

    public List<JobAndDecrementQuantity> LoadDecrementQuantitiesAfter(DateTime afterUTC)
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        {
          cmd.CommandText = "SELECT DecrementId,JobUnique,TimeUTC,Part,Quantity FROM job_decrements WHERE TimeUTC > $after";
          cmd.Parameters.Add("after", SqliteType.Integer).Value = afterUTC.Ticks;
          return LoadDecrementQuantitiesHelper(cmd);
        }
      }
    }

    private List<JobAndDecrementQuantity> LoadDecrementQuantitiesHelper(IDbCommand cmd)
    {
      var ret = new List<JobAndDecrementQuantity>();
      using (var reader = cmd.ExecuteReader())
      {
        while (reader.Read())
        {
          ret.Add(new JobAndDecrementQuantity()
          {
            DecrementId = reader.GetInt64(0),
            JobUnique = reader.GetString(1),
            TimeUTC = new DateTime(reader.GetInt64(2), DateTimeKind.Utc),
            Part = reader.GetString(3),
            Quantity = reader.GetInt32(4),
          });
        }
        return ret;
      }
    }
    #endregion

    #region Programs
    private Dictionary<(string prog, long rev), long> AddPrograms(IDbTransaction transaction, IEnumerable<ProgramEntry> programs, DateTime nowUtc)
    {
      if (programs == null || !programs.Any()) return new Dictionary<(string prog, long rev), long>();

      var negativeRevisionMap = new Dictionary<(string prog, long rev), long>();

      using (var checkCmd = _connection.CreateCommand())
      using (var checkMaxCmd = _connection.CreateCommand())
      using (var checkByCommentCmd = _connection.CreateCommand())
      using (var addProgCmd = _connection.CreateCommand())
      {
        ((IDbCommand)checkCmd).Transaction = transaction;
        checkCmd.CommandText = "SELECT ProgramContent FROM program_revisions WHERE ProgramName = $name AND ProgramRevision = $rev";
        checkCmd.Parameters.Add("name", SqliteType.Text);
        checkCmd.Parameters.Add("rev", SqliteType.Integer);

        ((IDbCommand)checkMaxCmd).Transaction = transaction;
        checkMaxCmd.CommandText = "SELECT ProgramRevision, ProgramContent FROM program_revisions WHERE ProgramName = $prog ORDER BY ProgramRevision DESC LIMIT 1";
        checkMaxCmd.Parameters.Add("prog", SqliteType.Text);

        ((IDbCommand)checkByCommentCmd).Transaction = transaction;
        checkByCommentCmd.CommandText = "SELECT ProgramRevision, ProgramContent FROM program_revisions " +
                                        " WHERE ProgramName = $prog AND RevisionComment IS NOT NULL AND RevisionComment = $comment " +
                                        " ORDER BY ProgramRevision DESC LIMIT 1";
        checkByCommentCmd.Parameters.Add("prog", SqliteType.Text);
        checkByCommentCmd.Parameters.Add("comment", SqliteType.Text);

        ((IDbCommand)addProgCmd).Transaction = transaction;
        addProgCmd.CommandText = "INSERT INTO program_revisions(ProgramName, ProgramRevision, RevisionTimeUTC, RevisionComment, ProgramContent) " +
                            " VALUES($name,$rev,$time,$comment,$prog)";
        addProgCmd.Parameters.Add("name", SqliteType.Text);
        addProgCmd.Parameters.Add("rev", SqliteType.Integer);
        addProgCmd.Parameters.Add("time", SqliteType.Integer).Value = nowUtc.Ticks;
        addProgCmd.Parameters.Add("comment", SqliteType.Text);
        addProgCmd.Parameters.Add("prog", SqliteType.Text);

        // positive revisions are either added or checked for match
        foreach (var prog in programs.Where(p => p.Revision > 0))
        {
          checkCmd.Parameters[0].Value = prog.ProgramName;
          checkCmd.Parameters[1].Value = prog.Revision;
          var content = checkCmd.ExecuteScalar();
          if (content != null && content != DBNull.Value)
          {
            if ((string)content != prog.ProgramContent)
            {
              throw new BadRequestException("Program " + prog.ProgramName + " rev" + prog.Revision.ToString() + " has already been used and the program contents do not match.");
            }
            // if match, do nothing
          }
          else
          {
            addProgCmd.Parameters[0].Value = prog.ProgramName;
            addProgCmd.Parameters[1].Value = prog.Revision;
            addProgCmd.Parameters[3].Value = string.IsNullOrEmpty(prog.Comment) ? DBNull.Value : (object)prog.Comment;
            addProgCmd.Parameters[4].Value = string.IsNullOrEmpty(prog.ProgramContent) ? DBNull.Value : (object)prog.ProgramContent;
            addProgCmd.ExecuteNonQuery();
          }
        }

        // zero and negative revisions are allocated a new number
        foreach (var prog in programs.Where(p => p.Revision <= 0).OrderByDescending(p => p.Revision))
        {
          long lastRev;
          checkMaxCmd.Parameters[0].Value = prog.ProgramName;
          using (var reader = checkMaxCmd.ExecuteReader())
          {
            if (reader.Read())
            {
              lastRev = reader.GetInt64(0);
              var lastContent = reader.GetString(1);
              if (lastContent == prog.ProgramContent)
              {
                if (prog.Revision < 0) negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = lastRev;
                continue;
              }
            }
            else
            {
              lastRev = 0;
            }
          }

          if (!string.IsNullOrEmpty(prog.Comment))
          {
            // check program matching the same comment
            checkByCommentCmd.Parameters[0].Value = prog.ProgramName;
            checkByCommentCmd.Parameters[1].Value = prog.Comment;
            using (var reader = checkByCommentCmd.ExecuteReader())
            {
              if (reader.Read())
              {
                var lastContent = reader.GetString(1);
                if (lastContent == prog.ProgramContent)
                {
                  if (prog.Revision < 0) negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = reader.GetInt64(0);
                  continue;
                }
              }

            }
          }

          addProgCmd.Parameters[0].Value = prog.ProgramName;
          addProgCmd.Parameters[1].Value = lastRev + 1;
          addProgCmd.Parameters[3].Value = string.IsNullOrEmpty(prog.Comment) ? DBNull.Value : (object)prog.Comment;
          addProgCmd.Parameters[4].Value = string.IsNullOrEmpty(prog.ProgramContent) ? DBNull.Value : (object)prog.ProgramContent;
          addProgCmd.ExecuteNonQuery();

          if (prog.Revision < 0) negativeRevisionMap[(prog: prog.ProgramName, rev: prog.Revision)] = lastRev + 1;
        }
      }

      return negativeRevisionMap;
    }

    private long? LatestRevisionForProgram(IDbTransaction trans, string program)
    {
      using (var cmd = _connection.CreateCommand())
      {
        ((IDbCommand)cmd).Transaction = trans;
        cmd.CommandText = "SELECT MAX(ProgramRevision) FROM program_revisions WHERE ProgramName = $prog";
        cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
        var rev = cmd.ExecuteScalar();
        if (rev == null || rev == DBNull.Value)
        {
          return null;
        }
        else
        {
          return (long)rev;
        }
      }
    }

    public ProgramRevision ProgramFromCellControllerProgram(string cellCtProgName)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          ProgramRevision prog = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT ProgramName, ProgramRevision, RevisionComment FROM program_revisions WHERE CellControllerProgramName = $prog LIMIT 1";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = cellCtProgName;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                prog = new ProgramRevision
                {
                  ProgramName = reader.GetString(0),
                  Revision = reader.GetInt64(1),
                  Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
                  CellControllerProgramName = cellCtProgName
                };
                break;
              }
            }
          }
          trans.Commit();
          return prog;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public ProgramRevision LoadProgram(string program, long revision)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          ProgramRevision prog = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT RevisionComment, CellControllerProgramName FROM program_revisions WHERE ProgramName = $prog AND ProgramRevision = $rev";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Integer).Value = revision;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                prog = new ProgramRevision
                {
                  ProgramName = program,
                  Revision = revision,
                  Comment = reader.IsDBNull(0) ? null : reader.GetString(0),
                  CellControllerProgramName = reader.IsDBNull(1) ? null : reader.GetString(1)
                };
                break;
              }
            }
          }
          trans.Commit();
          return prog;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public List<ProgramRevision> LoadProgramRevisionsInDescendingOrderOfRevision(string program, int count, long? startRevision)
    {
      count = Math.Min(count, 100);
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          if (startRevision.HasValue)
          {
            cmd.CommandText = "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions " +
                                " WHERE ProgramName = $prog AND ProgramRevision <= $rev " +
                                " ORDER BY ProgramRevision DESC " +
                                " LIMIT $cnt";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Integer).Value = startRevision.Value;
            cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
          }
          else
          {
            cmd.CommandText = "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions " +
                                " WHERE ProgramName = $prog " +
                                " ORDER BY ProgramRevision DESC " +
                                " LIMIT $cnt";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("cnt", SqliteType.Integer).Value = count;
          }

          using (var reader = cmd.ExecuteReader())
          {
            var ret = new List<ProgramRevision>();
            while (reader.Read())
            {
              ret.Add(new ProgramRevision
              {
                ProgramName = program,
                Revision = reader.GetInt64(0),
                Comment = reader.IsDBNull(1) ? null : reader.GetString(1),
                CellControllerProgramName = reader.IsDBNull(2) ? null : reader.GetString(2)
              });
            }
            return ret;
          }
        }
      }
    }

    public ProgramRevision LoadMostRecentProgram(string program)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          ProgramRevision prog = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions WHERE ProgramName = $prog ORDER BY ProgramRevision DESC LIMIT 1";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                prog = new ProgramRevision
                {
                  ProgramName = program,
                  Revision = reader.GetInt64(0),
                  Comment = reader.IsDBNull(1) ? null : reader.GetString(1),
                  CellControllerProgramName = reader.IsDBNull(2) ? null : reader.GetString(2)
                };
                break;
              }
            }
          }
          trans.Commit();
          return prog;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public string LoadProgramContent(string program, long revision)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          string content = null;
          using (var cmd = _connection.CreateCommand())
          {
            cmd.Transaction = trans;
            cmd.CommandText = "SELECT ProgramContent FROM program_revisions WHERE ProgramName = $prog AND ProgramRevision = $rev";
            cmd.Parameters.Add("prog", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Integer).Value = revision;
            using (var reader = cmd.ExecuteReader())
            {
              while (reader.Read())
              {
                if (!reader.IsDBNull(0))
                {
                  content = reader.GetString(0);
                }
                break;
              }
            }
          }
          trans.Commit();
          return content;
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    public List<ProgramRevision> LoadProgramsInCellController()
    {
      lock (_cfg)
      {
        using (var cmd = _connection.CreateCommand())
        using (var trans = _connection.BeginTransaction())
        {
          cmd.Transaction = trans;
          cmd.CommandText = "SELECT ProgramName, ProgramRevision, RevisionComment, CellControllerProgramName FROM program_revisions " +
                            " WHERE CellControllerProgramName IS NOT NULL";

          using (var reader = cmd.ExecuteReader())
          {
            var ret = new List<ProgramRevision>();
            while (reader.Read())
            {
              ret.Add(new ProgramRevision
              {
                ProgramName = reader.GetString(0),
                Revision = reader.GetInt64(1),
                Comment = reader.IsDBNull(2) ? null : reader.GetString(2),
                CellControllerProgramName = reader.IsDBNull(3) ? null : reader.GetString(3)
              });
            }
            return ret;
          }
        }
      }
    }

    public void SetCellControllerProgramForProgram(string program, long revision, string cellCtProgName)
    {
      lock (_cfg)
      {
        var trans = _connection.BeginTransaction();
        try
        {
          using (var cmd = _connection.CreateCommand())
          using (var checkCmd = _connection.CreateCommand())
          {
            if (!string.IsNullOrEmpty(cellCtProgName))
            {
              checkCmd.Transaction = trans;
              checkCmd.CommandText = "SELECT COUNT(*) FROM program_revisions WHERE CellControllerProgramName = $cell";
              checkCmd.Parameters.Add("cell", SqliteType.Text).Value = cellCtProgName;
              if ((long)checkCmd.ExecuteScalar() > 0)
              {
                throw new Exception("Cell program name " + cellCtProgName + " already in use");
              }
            }

            cmd.Transaction = trans;
            cmd.CommandText = "UPDATE program_revisions SET CellControllerProgramName = $cell WHERE ProgramName = $name AND ProgramRevision = $rev";
            cmd.Parameters.Add("cell", SqliteType.Text).Value = string.IsNullOrEmpty(cellCtProgName) ? DBNull.Value : (object)cellCtProgName;
            cmd.Parameters.Add("name", SqliteType.Text).Value = program;
            cmd.Parameters.Add("rev", SqliteType.Text).Value = revision;
            cmd.ExecuteNonQuery();
          }
          trans.Commit();
        }
        catch
        {
          trans.Rollback();
          throw;
        }
      }
    }

    #endregion
  }
}
