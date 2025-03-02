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
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Runtime.Serialization;

namespace BlackMaple.MachineFramework.Controllers
{
  [DataContract]
  public record QueuePosition
  {
    [DataMember(IsRequired = true)] public string Queue { get; init; }
    [DataMember(IsRequired = true)] public int Position { get; init; }
  }

  [ApiController]
  [Route("api/v1/[controller]")]
  public class jobsController : ControllerBase
  {
    private IFMSBackend _backend;

    public jobsController(IFMSBackend backend)
    {
      _backend = backend;
    }

    [HttpGet("history")]
    public HistoricData History([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
    {
      using (var db = _backend.OpenRepository())
      {
        return db.LoadJobHistory(startUTC, endUTC);
      }
    }

    [HttpGet("recent")]
    public HistoricData Recent([FromQuery] string afterScheduleId)
    {
      if (string.IsNullOrEmpty(afterScheduleId))
        throw new BadRequestException("After schedule ID must be non-empty");
      using (var db = _backend.OpenRepository())
      {
        return db.LoadJobsAfterScheduleId(afterScheduleId);
      }
    }

    [HttpGet("latest-schedule")]
    public PlannedSchedule LatestSchedule()
    {
      using (var db = _backend.OpenRepository())
      {
        return db.LoadMostRecentSchedule();
      }
    }

    [HttpGet("unfilled-workorders/by-part/{part}")]
    public IList<PartWorkorder> MostRecentUnfilledWorkordersForPart(string part)
    {
      if (string.IsNullOrEmpty(part))
        throw new BadRequestException("Part must be non-empty");
      using (var db = _backend.OpenRepository())
      {
        return db.MostRecentUnfilledWorkordersForPart(part);
      }
    }

    [DataContract]
    public record WorkordersAndPrograms
    {
      [DataMember(IsRequired = true)]
      public IReadOnlyList<PartWorkorder> Workorders { get; init; }
      [DataMember(IsRequired = false, EmitDefaultValue = false)]
      public IReadOnlyList<ProgramEntry> Programs { get; init; }
    }

    [HttpPut("unfilled-workorders/by-schid/{scheduleId}")]
    [ProducesResponseType(typeof(void), 200)]
    public void ReplaceWorkordersForScheduleId(string scheduleId, [FromBody] WorkordersAndPrograms workorders)
    {
      if (string.IsNullOrEmpty(scheduleId))
        throw new BadRequestException("ScheduleId must be non-empty");
      _backend.JobControl.ReplaceWorkordersForSchedule(scheduleId, workorders.Workorders, workorders.Programs);
    }

    [HttpGet("status")]
    public CurrentStatus CurrentStatus()
    {
      return _backend.JobControl.GetCurrentStatus();
    }

    [HttpPost("add")]
    [ProducesResponseType(typeof(void), 200)]
    public void Add([FromBody] NewJobs newJobs, [FromQuery] string expectedPreviousScheduleId, [FromQuery] bool waitForCopyToCell = true)
    {
      _backend.JobControl.AddJobs(newJobs, expectedPreviousScheduleId, waitForCopyToCell);
    }

    [HttpPost("part/{partName}/casting")]
    public InProcessMaterial AddUnallocatedCastingToQueueByPart(string partName, [FromQuery] string queue, [FromBody] string serial, [FromQuery] string operName = null)
    {
      if (string.IsNullOrEmpty(partName))
        throw new BadRequestException("Part name must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      return _backend.JobControl.AddUnallocatedPartToQueue(partName, queue, serial, operName);
    }

    [HttpPost("casting/{castingName}")]
    public List<InProcessMaterial> AddUnallocatedCastingToQueue(string castingName, [FromQuery] string queue, [FromBody] List<string> serials, [FromQuery] int qty = 1, [FromQuery] string operName = null)
    {
      if (string.IsNullOrEmpty(castingName))
        throw new BadRequestException("Casting name must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      return _backend.JobControl.AddUnallocatedCastingToQueue(castingName, qty, queue, serials, operName);
    }

    [HttpGet("job/{jobUnique}/plan")]
    public HistoricJob GetJobPlan(string jobUnique)
    {
      using (var db = _backend.OpenRepository())
      {
        return db.LoadJob(jobUnique);
      }
    }

    [HttpPost("job/{jobUnique}/unprocessed-material")]
    public InProcessMaterial AddUnprocessedMaterialToQueue(string jobUnique, [FromQuery] int lastCompletedProcess, [FromQuery] string queue, [FromQuery] int pos, [FromBody] string serial, [FromQuery] string operName = null)
    {
      if (string.IsNullOrEmpty(jobUnique))
        throw new BadRequestException("Job unique must be non-empty");
      if (string.IsNullOrEmpty(queue))
        throw new BadRequestException("Queue must be non-empty");
      if (lastCompletedProcess < 0) lastCompletedProcess = 0;
      return _backend.JobControl.AddUnprocessedMaterialToQueue(jobUnique, lastCompletedProcess, queue, pos, serial, operName);
    }

    [HttpPut("job/{jobUnique}/comment")]
    [ProducesResponseType(typeof(void), 200)]
    public void SetJobComment(string jobUnique, [FromBody] string comment)
    {
      _backend.JobControl.SetJobComment(jobUnique, comment);
    }

    [HttpPut("material/{materialId}/queue")]
    [ProducesResponseType(typeof(void), 200)]
    public void SetMaterialInQueue(long materialId, [FromBody] QueuePosition queue, [FromQuery] string operName = null)
    {
      if (string.IsNullOrEmpty(queue.Queue))
        throw new BadRequestException("Queue name must be non-empty");
      _backend.JobControl.SetMaterialInQueue(materialId, queue.Queue, queue.Position, operName);
    }

    [HttpDelete("material/{materialId}/queue")]
    [ProducesResponseType(typeof(void), 200)]
    public void RemoveMaterialFromAllQueues(long materialId, [FromQuery] string operName = null)
    {
      _backend.JobControl.RemoveMaterialFromAllQueues(new[] { materialId }, operName);
    }

    [HttpPut("material/{materialId}/quarantine")]
    [ProducesResponseType(typeof(void), 200)]
    public void SignalMaterialForQuarantine(long materialId, [FromBody] string queue, [FromQuery] string operName = null)
    {
      _backend.JobControl.SignalMaterialForQuarantine(materialId, queue, operName);
    }

    [HttpPut("material/{materialId}/invalidate-process")]
    [ProducesResponseType(typeof(void), 200)]
    public void InvalidatePalletCycle(long materialId, [FromBody] int process, [FromQuery] string putMatInQueue = null, [FromQuery] string operName = null)
    {
      _backend.JobControl.InvalidatePalletCycle(matId: materialId, process: process, oldMatPutInQueue: putMatInQueue, operatorName: operName);
    }

    [DataContract]
    public record MatToPutOnPallet
    {
      [DataMember(IsRequired = true)] public string Pallet { get; init; }
      [DataMember(IsRequired = true)] public long MaterialIDToSetOnPallet { get; init; }
    }

    [HttpPut("material/{materialId}/swap-off-pallet")]
    [ProducesResponseType(typeof(void), 200)]
    public void SwapMaterialOnPallet(long materialId, [FromBody] MatToPutOnPallet mat, [FromQuery] string operName = null)
    {
      _backend.JobControl.SwapMaterialOnPallet(
        oldMatId: materialId,
        newMatId: mat.MaterialIDToSetOnPallet,
        pallet: mat.Pallet,
        operatorName: operName
      );
    }

    [HttpDelete("material")]
    [ProducesResponseType(typeof(void), 200)]
    public void BulkRemoveMaterialFromQueues([FromBody] List<long> id, [FromQuery] string operName = null)
    {
      if (id == null || id.Count == 0) return;
      _backend.JobControl.RemoveMaterialFromAllQueues(id, operName);
    }

    [HttpDelete("planned-cycles")]
    public List<JobAndDecrementQuantity> DecrementQuantities(
        [FromQuery] long? loadDecrementsStrictlyAfterDecrementId = null,
        [FromQuery] DateTime? loadDecrementsAfterTimeUTC = null)
    {
      if (loadDecrementsStrictlyAfterDecrementId != null)
        return _backend.JobControl.DecrementJobQuantites(loadDecrementsStrictlyAfterDecrementId ?? 0);
      else if (loadDecrementsAfterTimeUTC.HasValue)
        return _backend.JobControl.DecrementJobQuantites(loadDecrementsAfterTimeUTC.Value);
      else
        throw new BadRequestException("Must specify either loadDecrementsStrictlyAfterDecrementId or loadDecrementsAfterTimeUTC");
    }
  }
}