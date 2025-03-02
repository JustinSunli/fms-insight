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

using System;
using System.Linq;
using System.Collections.Generic;
using BlackMaple.MachineFramework;
using BlackMaple.MachineWatchInterface;

namespace MazakMachineInterface
{

  public class BuildMazakSchedules
  {

    public static (MazakWriteData, ISet<string>)
      RemoveCompletedSchedules(MazakCurrentStatus mazakData)
    {
      //remove all completed production
      var schs = new List<MazakScheduleRow>();
      var savedParts = new HashSet<string>();
      foreach (var schRow in mazakData.Schedules)
      {
        if (schRow.PlanQuantity == schRow.CompleteQuantity)
        {
          var newSchRow = schRow with
          {
            Command = MazakWriteCommand.Delete
          };
          schs.Add(newSchRow);
        }
        else
        {
          savedParts.Add(schRow.PartName);
        }
      }
      var transSet = new MazakWriteData() { Schedules = schs };
      return (transSet, savedParts);
    }

    public static MazakWriteData AddSchedules(
      MazakAllData mazakData,
      IEnumerable<JobPlan> jobs,
      bool UseStartingOffsetForDueDate)
    {
      if (!jobs.Any()) return new MazakWriteData();

      var schs = new List<MazakScheduleRow>();
      var routeStartDate = jobs.First().RouteStartingTimeUTC.ToLocalTime().Date;

      var usedScheduleIDs = new HashSet<int>();
      var scheduledParts = new HashSet<string>();
      var maxPriMatchingDate = 9;
      foreach (var schRow in mazakData.Schedules)
      {
        usedScheduleIDs.Add(schRow.Id);
        scheduledParts.Add(schRow.PartName);
        if (schRow.DueDate == routeStartDate)
        {
          maxPriMatchingDate = Math.Max(maxPriMatchingDate, schRow.Priority);
        }
      }

      //now add the new schedule
      foreach (JobPlan part in jobs)
      {
        // 1 path per job should have been already prevented by an earlier check
        if (part.GetNumPaths(1) > 1) continue;
        if (part.GetPlannedCyclesOnFirstProcess() <= 0) continue;

        //check if part exists downloaded
        int downloadUid = -1;
        string mazakPartName = "";
        string mazakComment = "";
        foreach (var partRow in mazakData.Parts)
        {
          if (MazakPart.IsSailPart(partRow.PartName, partRow.Comment))
          {
            MazakPart.ParseComment(partRow.Comment, out string u, out var ps, out bool m);
            if (u == part.UniqueStr && ps.PathForProc(proc: 1) == 1)
            {
              downloadUid = MazakPart.ParseUID(partRow.PartName);
              mazakPartName = partRow.PartName;
              mazakComment = partRow.Comment;
              break;
            }
          }
        }
        if (downloadUid < 0)
        {
          throw new BlackMaple.MachineFramework.BadRequestException(
            "Attempting to create schedule for " + part.UniqueStr + " but a part does not exist");
        }

        if (!scheduledParts.Contains(mazakPartName))
        {
          int schid = FindNextScheduleId(usedScheduleIDs);
          int earlierConflicts = CountEarlierConflicts(part, jobs);
          schs.Add(SchedulePart(
            SchID: schid,
            mazakPartName: mazakPartName,
            mazakComment: mazakComment,
            numProcess: part.NumProcesses,
            part: part,
            earlierConflicts: earlierConflicts,
            startingPriority: maxPriMatchingDate + 1,
            routeStartDate: routeStartDate,
            UseStartingOffsetForDueDate: UseStartingOffsetForDueDate));
        }
      }

      if (UseStartingOffsetForDueDate)
        return new MazakWriteData() { Schedules = SortSchedulesByDate(schs) };
      else
        return new MazakWriteData() { Schedules = schs };
    }

    private static MazakScheduleRow SchedulePart(
      int SchID, string mazakPartName, string mazakComment, int numProcess,
      JobPlan part, int earlierConflicts, int startingPriority, DateTime routeStartDate, bool UseStartingOffsetForDueDate)
    {
      bool entireHold = false;
      if (part.HoldEntireJob != null) entireHold = part.HoldEntireJob.IsJobOnHold;
      bool machiningHold = false;
      if (part.HoldMachining(1, 1) != null) machiningHold = part.HoldMachining(1, 1).IsJobOnHold;

      var newSchRow = new MazakScheduleRow()
      {
        Command = MazakWriteCommand.Add,
        Id = SchID,
        PartName = mazakPartName,
        PlanQuantity = part.GetPlannedCyclesOnFirstProcess(),
        CompleteQuantity = 0,
        FixForMachine = 0,
        MissingFixture = 0,
        MissingProgram = 0,
        MissingTool = 0,
        MixScheduleID = 0,
        ProcessingPriority = 0,
        Comment = mazakComment,
        Priority = 75,
        DueDate = DateTime.Parse("1/1/2008 12:00:00 AM"),
        HoldMode = (int)HoldPattern.CalculateHoldMode(entireHold, machiningHold),
      };

      if (UseStartingOffsetForDueDate)
      {
        if (part.GetSimulatedStartingTimeUTC(1, 1) != DateTime.MinValue)
        {
          var start = part.GetSimulatedStartingTimeUTC(1, 1);
          newSchRow = newSchRow with
          {
            DueDate = routeStartDate,
            Priority = Math.Min(100, startingPriority + earlierConflicts)
          };
        }
        else
        {
          newSchRow = newSchRow with
          {
            DueDate = routeStartDate,
            Priority = startingPriority,
          };
        }
      }

      int matQty = newSchRow.PlanQuantity;

      if (!string.IsNullOrEmpty(part.GetInputQueue(process: 1, path: 1)))
      {
        matQty = 0;
      }

      //need to add all the ScheduleProcess rows
      for (int i = 1; i <= numProcess; i++)
      {
        var newSchProcRow = new MazakScheduleProcessRow()
        {
          MazakScheduleRowId = SchID,
          ProcessNumber = i,
          ProcessMaterialQuantity = (i == 1) ? matQty : 0,
          ProcessBadQuantity = 0,
          ProcessExecuteQuantity = 0,
          ProcessMachine = 0,
        };

        newSchRow.Processes.Add(newSchProcRow);
      }

      return newSchRow;
    }

    /// Count up how many JobPaths have an earlier simulation start time and also share a fixture/face with the current job
    private static int CountEarlierConflicts(JobPlan jobToCheck, IEnumerable<JobPlan> jobs)
    {
      var startT = jobToCheck.GetSimulatedStartingTimeUTC(process: 1, path: 1);
      if (startT == DateTime.MinValue) return 0;

      // first, calculate the fixtures and faces used by the job to check
      var usedFixtureFaces = new HashSet<ValueTuple<string, string>>();
      var usedPallets = new HashSet<string>();
      for (int proc = 1; proc <= jobToCheck.NumProcesses; proc++)
      {
        var (plannedFix, plannedFace) = jobToCheck.PlannedFixture(proc, 1);
        if (string.IsNullOrEmpty(plannedFix))
        {
          foreach (var p in jobToCheck.PlannedPallets(proc, 1))
          {
            usedPallets.Add(p);
          }
        }
        else
        {
          usedFixtureFaces.Add((plannedFix, plannedFace.ToString()));
        }
      }

      int earlierConflicts = 0;
      // go through each other job
      foreach (var otherJob in jobs)
      {
        if (otherJob.UniqueStr == jobToCheck.UniqueStr) continue;

        // see if the process 1 starting time is later and if so skip the remaining checks
        var otherStart = otherJob.GetSimulatedStartingTimeUTC(process: 1, path: 1);
        if (otherStart == DateTime.MinValue) continue;
        if (otherStart >= startT) continue;

        //the job starts earlier than the jobToCheck, but need to see if it conflicts.

        // go through all processes and if a fixture face matches, count it as a conflict.
        for (var otherProc = 1; otherProc <= otherJob.NumProcesses; otherProc++)
        {
          var (otherFix, otherFace) = otherJob.PlannedFixture(otherProc, 1);
          if (usedFixtureFaces.Contains((otherFix, otherFace.ToString())))
          {
            earlierConflicts += 1;
            goto checkNextPath;
          }
          if (otherJob.PlannedPallets(otherProc, 1).Any(usedPallets.Contains))
          {
            earlierConflicts += 1;
            goto checkNextPath;
          }
        }

      checkNextPath:;
      }

      return earlierConflicts;
    }

    private static IReadOnlyList<MazakScheduleRow> SortSchedulesByDate(List<MazakScheduleRow> schs)
    {
      return schs
        .OrderBy(x => x.DueDate)
        .ThenBy(x => -x.Priority)
        .ToList();
    }

    private static int FindNextScheduleId(HashSet<int> usedScheduleIds)
    {
      for (int i = 1; i <= 9999; i++)
      {
        if (!usedScheduleIds.Contains(i))
        {
          usedScheduleIds.Add(i);
          return i;
        }
      }
      throw new Exception("All Schedule Ids are currently being used");
    }

  }
}