/* Copyright (c) 2018, John Lenz

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
using BlackMaple.MachineWatchInterface;

namespace MachineWatchApiServer.Controllers
{
    [Route("api/v1/[controller]")]
    public class logController : ControllerBase
    {
        private ILogDatabase _server;

        public logController(IServerBackend backend)
        {
            _server = backend.LogDatabase();
        }

        [HttpGet("events/all")]
        public List<LogEntry> Get([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _server.GetLogEntries(startUTC, endUTC);
        }

        [HttpGet("events/all-completed-parts")]
        public List<LogEntry> GetCompletedParts([FromQuery] DateTime startUTC, [FromQuery] DateTime endUTC)
        {
            return _server.GetCompletedPartLogs(startUTC, endUTC);
        }

        [HttpGet("events/recent")]
        public List<LogEntry> Recent([FromQuery] long lastSeenCounter)
        {
            return _server.GetLog(lastSeenCounter);
        }

        [HttpGet("events/for-material/{materialID}")]
        public List<LogEntry> LogForMaterial(long materialID)
        {
            return _server.GetLogForMaterial(materialID);
        }

        [HttpGet("events/for-serial/{serial}")]
        public List<LogEntry> LogForSerial(string serial)
        {
            return _server.GetLogForSerial(serial);
        }

        [HttpGet("events/for-workorder/{workorder}")]
        public List<LogEntry> LogForWorkorder(string workorder)
        {
            return _server.GetLogForWorkorder(workorder);
        }

        [HttpGet("workorders")]
        public List<WorkorderSummary> GetWorkorders([FromBody] IEnumerable<string> workorderIds)
        {
            return _server.GetWorkorderSummaries(workorderIds);
        }

        [HttpPost("serial/{serial}/material")]
        public LogEntry SetSerial(string serial, [FromBody] LogMaterial mat)
        {
            return _server.RecordSerialForMaterialID(mat, serial);
        }

        [HttpPost("workorder/{workorder}/material")]
        public LogEntry SetWorkorder(string workorder, [FromBody] LogMaterial mat)
        {
            return _server.RecordWorkorderForMaterialID(mat, workorder);
        }

        [HttpPost("workorder/{workorder}/finalize")]
        public LogEntry FinalizeWorkorder(string workorder)
        {
            return _server.RecordFinalizedWorkorder(workorder);
        }

        [HttpGet("settings/serials")]
        public SerialSettings GetSerialSettings()
        {
            return _server.GetSerialSettings();
        }

        [HttpPut("settings/serials")]
        public void SetSerialSettings([FromBody] SerialSettings settings)
        {
            _server.SetSerialSettings(settings);
        }
    }
}