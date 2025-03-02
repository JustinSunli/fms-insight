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

import { addHours, differenceInMinutes, addMinutes } from "date-fns";

import { fakeCycle, fakeMaterial } from "../../test/events.fake";
import { ILogEntry, LogType } from "../network/api";
import { LazySeq } from "../util/lazyseq";
import { binActiveCyclesByDayAndStat, binOccupiedCyclesByDayAndStat, buildOeeHeatmapTable } from "./results.oee";
import { snapshot_UNSTABLE } from "recoil";
import { applyConduitToSnapshot } from "../util/recoil-util";
import { onLoadLast30Log } from "../cell-status/loading";
import { last30StationCycles } from "../cell-status/station-cycles";

it("bins actual cycles by day", () => {
  const now = new Date(2018, 2, 5); // midnight in local time
  const nowChicago = new Date(Date.UTC(2018, 2, 5, 6, 0, 0)); // America/Chicago time
  const minOffset = differenceInMinutes(nowChicago, now);

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15 }),
    LazySeq.ofRange(1, 3)
      .map((i) => {
        const material = [fakeMaterial()];
        return {
          counter: 1,
          material,
          pal: "2",
          type: LogType.LoadUnloadCycle,
          startofcycle: false,
          endUTC: addHours(now, -4), // same time for both cycles
          loc: "L/U",
          locnum: 3,
          result: i === 1 ? "LOAD" : "UNLOAD",
          program: i === 1 ? "LOAD" : "UNLOAD",
          elapsed: "PT8M",
          active: "PT3M",
        };
      })
      .toArray()
  );

  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  const cycles = snapshot.getLoadable(last30StationCycles).valueOrThrow();

  let byDayAndStat = binActiveCyclesByDayAndStat(cycles);

  // update day to be in Chicago timezone
  // This is because the snapshot formats the day as a UTC time in Chicago timezone
  // Note this is after cycles are binned, which is correct since cycles are generated using
  // now in local time and then binned in local time.  Just need to update the date before
  // comparing with the snapshot
  byDayAndStat = byDayAndStat.map((dayAndStat, val) => [dayAndStat.adjustDay((d) => addMinutes(d, minOffset)), val]);

  expect(byDayAndStat).toMatchSnapshot("cycles binned by day and station");

  byDayAndStat = binOccupiedCyclesByDayAndStat(cycles);
  byDayAndStat = byDayAndStat.map((dayAndStat, val) => [dayAndStat.adjustDay((d) => addMinutes(d, minOffset)), val]);

  expect(byDayAndStat).toMatchSnapshot("occupied cycles binned by day and station");
});

it("creates points clipboard table", () => {
  // table formats columns in local time, so no need to convert to a specific timezone
  const now = new Date(2018, 2, 5); // midnight in local time

  const evts = ([] as ILogEntry[]).concat(
    fakeCycle({ time: now, machineTime: 30 }),
    fakeCycle({ time: addHours(now, -3), machineTime: 20 }),
    fakeCycle({ time: addHours(now, -15), machineTime: 15 })
  );

  const snapshot = applyConduitToSnapshot(snapshot_UNSTABLE(), onLoadLast30Log, evts);
  const cycles = snapshot.getLoadable(last30StationCycles).valueOrThrow();

  const byDayAndStat = binActiveCyclesByDayAndStat(cycles);

  const points = LazySeq.ofIterable(byDayAndStat)
    .map(([dayAndStat, val]) => ({
      x: dayAndStat.day,
      y: dayAndStat.station,
      label: val.toString(),
    }))
    .toArray();

  const table = document.createElement("div");
  table.innerHTML = buildOeeHeatmapTable("Station", points);
  expect(table).toMatchSnapshot("clipboard table");
});
