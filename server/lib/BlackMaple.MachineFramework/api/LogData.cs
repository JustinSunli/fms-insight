/* Copyright (c) 2020, John Lenz

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

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Germinate;
using System.Collections.Immutable;

namespace BlackMaple.MachineFramework
{
  [DataContract, Draftable]
  public record LogMaterial
  {
    [DataMember(Name = "id", IsRequired = true)]
    public long MaterialID { get; init; }

    [DataMember(Name = "uniq", IsRequired = true)]
    public string JobUniqueStr { get; init; } = "";

    [DataMember(Name = "part", IsRequired = true)]
    public string PartName { get; init; } = "";

    [DataMember(Name = "proc", IsRequired = true)]
    public int Process { get; init; }

    [DataMember(Name = "numproc", IsRequired = true)]
    public int NumProcesses { get; init; }

    [DataMember(Name = "face", IsRequired = true)]
    public string Face { get; init; } = "";

    [DataMember(Name = "serial", IsRequired = false, EmitDefaultValue = false)]
    public string? Serial { get; init; }

    [DataMember(Name = "workorder", IsRequired = false, EmitDefaultValue = false)]
    public string? Workorder { get; init; }

    public LogMaterial() { }

    public LogMaterial(long matID, string uniq, int proc, string part, int numProc, string serial, string workorder, string face)
    {
      MaterialID = matID;
      JobUniqueStr = uniq;
      PartName = part;
      Process = proc;
      NumProcesses = numProc;
      Face = face;
      Serial = serial;
      Workorder = workorder;
    }

    public static LogMaterial operator %(LogMaterial m, Action<ILogMaterialDraft> f) => m.Produce(f);
  }

  [DataContract]
  public enum LogType
  {
    [EnumMember] LoadUnloadCycle = 1, //numbers are for backwards compatibility with old type enumeration
    [EnumMember] MachineCycle = 2,
    [EnumMember] PartMark = 6,
    [EnumMember] Inspection = 7,
    [EnumMember] OrderAssignment = 10,
    [EnumMember] GeneralMessage = 100,
    [EnumMember] PalletCycle = 101,
    [EnumMember] FinalizeWorkorder = 102,
    [EnumMember] InspectionResult = 103,
    [EnumMember] Wash = 104,
    [EnumMember] AddToQueue = 105,
    [EnumMember] RemoveFromQueue = 106,
    [EnumMember] InspectionForce = 107,
    [EnumMember] PalletOnRotaryInbound = 108,
    [EnumMember] PalletInStocker = 110,
    [EnumMember] SignalQuarantine = 111,
    [EnumMember] InvalidateCycle = 112,
    [EnumMember] SwapMaterialOnPallet = 113,
    // when adding types, must also update the convertLogType() function in client/backup-viewer/src/background.ts
  }

  [DataContract, Draftable, KnownType(typeof(MaterialProcessActualPath))]
  public record LogEntry
  {
    [DataMember(Name = "counter", IsRequired = true)]
    public long Counter { get; init; }

    [DataMember(Name = "material", IsRequired = true)]
    public ImmutableList<LogMaterial> Material { get; init; } = ImmutableList<LogMaterial>.Empty;

    [DataMember(Name = "type", IsRequired = true)]
    public LogType LogType { get; init; }

    [DataMember(Name = "startofcycle", IsRequired = true)]
    public bool StartOfCycle { get; init; }

    [DataMember(Name = "endUTC", IsRequired = true)]
    public DateTime EndTimeUTC { get; init; }

    [DataMember(Name = "loc", IsRequired = true)]
    public string LocationName { get; init; } = "";

    [DataMember(Name = "locnum", IsRequired = true)]
    public int LocationNum { get; init; }

    [DataMember(Name = "pal", IsRequired = true)]
    public string Pallet { get; init; } = "";

    [DataMember(Name = "program", IsRequired = true)]
    public string Program { get; init; } = "";

    [DataMember(Name = "result", IsRequired = true)]
    public string Result { get; init; } = "";

    // End of route is kept only for backwards compatbility.
    // Instead, the user who is processing the data should determine what event
    // to use to determine when the material should be considered "complete"
    [IgnoreDataMember]
    public bool EndOfRoute { get; init; }

    [DataMember(Name = "elapsed", IsRequired = true)]
    public TimeSpan ElapsedTime { get; init; } //time from cycle-start to cycle-stop

    [DataMember(Name = "active", IsRequired = true)]
    public TimeSpan ActiveOperationTime { get; init; } //time that the machining or operation is actually active

    [DataMember(Name = "details", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableDictionary<string, string>? ProgramDetails { get; init; } = ImmutableDictionary<string, string>.Empty;

    [DataMember(Name = "tools", IsRequired = false, EmitDefaultValue = false)]
    public ImmutableDictionary<string, ToolUse>? Tools { get; init; } = ImmutableDictionary<string, ToolUse>.Empty;

    public LogEntry() { }

    public LogEntry(
        long cntr,
        IEnumerable<LogMaterial> mat,
        string pal,
        LogType ty,
        string locName,
        int locNum,
        string prog,
        bool start,
        DateTime endTime,
        string result,
        bool endOfRoute)
        : this(cntr, mat, pal, ty, locName, locNum, prog, start, endTime, result, endOfRoute,
              TimeSpan.FromMinutes(-1), TimeSpan.Zero)
    { }

    public LogEntry(
        long cntr,
        IEnumerable<LogMaterial> mat,
        string pal,
        LogType ty,
        string locName,
        int locNum,
        string prog,
        bool start,
        DateTime endTime,
        string result,
        bool endOfRoute,
        TimeSpan elapsed,
        TimeSpan active)
    {
      Counter = cntr;
      Material = mat.ToImmutableList();
      Pallet = pal;
      LogType = ty;
      LocationName = locName;
      LocationNum = locNum;
      Program = prog;
      StartOfCycle = start;
      EndTimeUTC = endTime;
      Result = result;
      EndOfRoute = endOfRoute;
      ElapsedTime = elapsed;
      ActiveOperationTime = active;
      ProgramDetails = ImmutableDictionary<string, string>.Empty;
      Tools = ImmutableDictionary<string, ToolUse>.Empty;
    }

    public bool ShouldSerializeProgramDetails()
    {
      return ProgramDetails != null && ProgramDetails.Count > 0;
    }

    public bool ShouldSerializeTools()
    {
      return Tools != null && Tools.Count > 0;
    }

    public static LogEntry operator %(LogEntry e, Action<ILogEntryDraft> f) => e.Produce(f);
  }

  // stored serialized in json format in the details for inspection logs.
  [DataContract, Draftable]
  public record MaterialProcessActualPath
  {
    [DataContract]
    public record Stop
    {
      [DataMember(IsRequired = true)] public string StationName { get; init; } = "";
      [DataMember(IsRequired = true)] public int StationNum { get; init; }
    }

    [DataMember(IsRequired = true)] public long MaterialID { get; init; }
    [DataMember(IsRequired = true)] public int Process { get; init; }
    [DataMember(IsRequired = true)] public string Pallet { get; init; } = "";
    [DataMember(IsRequired = true)] public int LoadStation { get; init; }
    [DataMember(IsRequired = true)] public ImmutableList<Stop> Stops { get; init; } = ImmutableList<Stop>.Empty;
    [DataMember(IsRequired = true)] public int UnloadStation { get; init; }

    public static MaterialProcessActualPath operator %(MaterialProcessActualPath m, Action<IMaterialProcessActualPathDraft> f)
       => m.Produce(f);
  }

  [DataContract]
  public record EditMaterialInLogEvents
  {
    [DataMember(IsRequired = true)]
    public long OldMaterialID { get; init; }

    [DataMember(IsRequired = true)]
    public long NewMaterialID { get; init; }

    [DataMember(IsRequired = true)]
    public IEnumerable<LogEntry> EditedEvents { get; init; } = new LogEntry[] { };
  }
}
