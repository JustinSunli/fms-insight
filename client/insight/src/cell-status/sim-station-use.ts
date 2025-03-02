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
import { durationToMinutes } from "../util/parseISODuration";
import { LazySeq } from "../util/lazyseq";
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import * as L from "list/methods";
import { addDays } from "date-fns";
import type { ServerEventAndTime } from "./loading";
import { conduit } from "../util/recoil-util";
import { IHistoricData, ISimulatedStationUtilization } from "../network/api";

export interface SimStationUse {
  readonly station: string;
  readonly start: Date;
  readonly end: Date;
  readonly utilizationTime: number;
  readonly plannedDownTime: number;
}

const last30SimStationUseRW = atom<L.List<SimStationUse>>({
  key: "last30SimStationUse",
  default: L.empty(),
});
export const last30SimStationUse: RecoilValueReadOnly<L.List<SimStationUse>> = last30SimStationUseRW;

const specificMonthSimStationUseRW = atom<L.List<SimStationUse>>({
  key: "specificMonthSimStationUse",
  default: L.empty(),
});
export const specificMonthSimStationUse: RecoilValueReadOnly<L.List<SimStationUse>> = specificMonthSimStationUseRW;

function procSimUse(apiSimUse: ReadonlyArray<ISimulatedStationUtilization>): L.List<SimStationUse> {
  return L.from(apiSimUse).map((simUse) => ({
    station: simUse.stationGroup + " #" + simUse.stationNum.toString(),
    start: simUse.startUTC,
    end: simUse.endUTC,
    utilizationTime: durationToMinutes(simUse.utilizationTime),
    plannedDownTime: durationToMinutes(simUse.plannedDownTime),
  }));
}

export const setLast30SimStatUse = conduit<Readonly<IHistoricData>>(
  (t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
    t.set(last30SimStationUseRW, (oldSimUse) => oldSimUse.concat(procSimUse(history.stationUse)));
  }
);

export const updateLast30SimStatUse = conduit<ServerEventAndTime>(
  (t: TransactionInterface_UNSTABLE, { evt, now, expire }: ServerEventAndTime) => {
    if (evt.newJobs?.stationUse) {
      const apiSimUse = evt.newJobs?.stationUse;
      t.set(last30SimStationUseRW, (simUse) => {
        if (expire) {
          const expireT = addDays(now, -30);
          // check if nothing to expire and no new data
          const minStat = LazySeq.ofIterable(simUse).minOn((e) => e.end.getTime());
          if ((minStat.isNone() || minStat.get().start >= expireT) && apiSimUse.length === 0) {
            return simUse;
          }

          simUse = simUse.filter((e) => e.start >= expireT);
        }

        return simUse.concat(procSimUse(apiSimUse));
      });
    }
  }
);

export const setSpecificMonthSimStatUse = conduit<Readonly<IHistoricData>>(
  (t: TransactionInterface_UNSTABLE, history: Readonly<IHistoricData>) => {
    t.set(specificMonthSimStationUseRW, procSimUse(history.stationUse));
  }
);
