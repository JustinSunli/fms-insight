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
import { differenceInSeconds } from "date-fns";
import { fieldsHashCode, HashMap, Vector } from "prelude-ts";
import { atom, RecoilValueReadOnly, TransactionInterface_UNSTABLE } from "recoil";
import { ILogEntry, LogType } from "../network/api";
import { LazySeq } from "../util/lazyseq";
import { durationToMinutes } from "../util/parseISODuration";
import { conduit } from "../util/recoil-util";

export interface StatisticalCycleTime {
  readonly medianMinutesForSingleMat: number;
  readonly MAD_belowMinutes: number; // MAD of points below the median
  readonly MAD_aboveMinutes: number; // MAD of points above the median
  readonly expectedCycleMinutesForSingleMat: number;
}

export class PartAndStationOperation {
  public constructor(
    public readonly part: string,
    public readonly proc: number,
    public readonly statGroup: string,
    public readonly operation: string
  ) {}
  public static ofLogCycle(c: Readonly<ILogEntry>): PartAndStationOperation {
    return new PartAndStationOperation(
      c.material[0].part,
      c.material[0].proc,
      c.loc,
      c.type === LogType.LoadUnloadCycle ? c.result : c.program
    );
  }
  equals(other: PartAndStationOperation): boolean {
    return (
      this.part === other.part &&
      this.proc === other.proc &&
      this.statGroup === other.statGroup &&
      this.operation === other.operation
    );
  }
  hashCode(): number {
    return fieldsHashCode(this.part, this.proc, this.statGroup, this.operation);
  }
  toString(): string {
    return `{part: ${this.part}}, proc: ${this.proc}, statGroup: ${this.statGroup}, operation: ${this.operation}}`;
  }
}

export type EstimatedCycleTimes = HashMap<PartAndStationOperation, StatisticalCycleTime>;

const last30EstimatedTimesRW = atom<EstimatedCycleTimes>({
  key: "last30Estimatedcycletimes",
  default: HashMap.empty(),
});
export const last30EstimatedCycleTimes: RecoilValueReadOnly<EstimatedCycleTimes> = last30EstimatedTimesRW;

const specificMonthEstimatedTimesRW = atom<EstimatedCycleTimes>({
  key: "specificMonthEstimatedcycleTimes",
  default: HashMap.empty(),
});
export const specificMonthEstimatedCycleTimes: RecoilValueReadOnly<EstimatedCycleTimes> = specificMonthEstimatedTimesRW;

// Assume: samples come from two distributions:
//  - the program runs without interruption, giving a guassian iid around the cycle time.
//  - the program is interrupted or stopped, which adds a random amount to the program
//    and results in an outlier.
//  - the program doesn't run at all, which results in a random short cycle time.
// We use median absolute deviation to detect outliers, remove the outliers,
// then compute average to find cycle time.

export function isOutlier(s: StatisticalCycleTime, mins: number): boolean {
  if (s.medianMinutesForSingleMat === 0) {
    return false;
  }
  if (mins < s.medianMinutesForSingleMat) {
    return (s.medianMinutesForSingleMat - mins) / s.MAD_belowMinutes > 2;
  } else {
    return (mins - s.medianMinutesForSingleMat) / s.MAD_aboveMinutes > 2;
  }
}

function median(vals: LazySeq<number>): number {
  const sorted = vals.toArray().sort();
  const cnt = sorted.length;
  if (cnt === 0) {
    return 0;
  }
  const half = Math.floor(sorted.length / 2);
  if (sorted.length % 2 === 0) {
    // average two middle
    return (sorted[half - 1] + sorted[half]) / 2;
  } else {
    // return middle
    return sorted[half];
  }
}

function estimateCycleTimes(cycles: Iterable<number>): StatisticalCycleTime {
  // compute median
  const medianMinutes = median(LazySeq.ofIterable(cycles));

  // absolute deviation from median, but use different values for below and above
  // median.  Below is assumed to be from fake cycles and above is from interrupted programs.
  // since we assume gaussian, use consistantcy constant of 1.4826

  let madBelowMinutes =
    1.4826 *
    median(
      LazySeq.ofIterable(cycles)
        .filter((x) => x <= medianMinutes)
        .map((x) => medianMinutes - x)
    );
  // clamp at 15 seconds
  if (madBelowMinutes < 0.25) {
    madBelowMinutes = 0.25;
  }

  let madAboveMinutes =
    1.4826 *
    median(
      LazySeq.ofIterable(cycles)
        .filter((x) => x >= medianMinutes)
        .map((x) => x - medianMinutes)
    );
  // clamp at 15 seconds
  if (madAboveMinutes < 0.25) {
    madAboveMinutes = 0.25;
  }

  const statCycleTime = {
    medianMinutesForSingleMat: medianMinutes,
    MAD_belowMinutes: madBelowMinutes,
    MAD_aboveMinutes: madAboveMinutes,
    expectedCycleMinutesForSingleMat: 0,
  };

  // filter to only inliers
  const inliers = LazySeq.ofIterable(cycles)
    .filter((x) => !isOutlier(statCycleTime, x))
    .toArray();
  // compute average of inliers
  const expectedCycleMinutesForSingleMat = inliers.reduce((sum, x) => sum + x, 0) / inliers.length;

  return { ...statCycleTime, expectedCycleMinutesForSingleMat };
}

export function chunkCyclesWithSimilarEndTime<T>(cycles: Vector<T>, getTime: (c: T) => Date): Vector<ReadonlyArray<T>> {
  const sorted = cycles.sortOn((c) => getTime(c).getTime());
  return Vector.ofIterable(
    LazySeq.ofIterator(function* () {
      let chunk: Array<T> = [];
      for (const c of sorted) {
        if (chunk.length === 0) {
          chunk = [c];
        } else if (differenceInSeconds(getTime(c), getTime(chunk[chunk.length - 1])) < 10) {
          chunk.push(c);
        } else {
          yield chunk;
          chunk = [c];
        }
      }
      if (chunk.length > 0) {
        yield chunk;
      }
    })
  );
}

export interface LogEntryWithSplitElapsed<T> {
  readonly cycle: T;
  readonly elapsedForSingleMaterialMinutes: number;
}

export function splitElapsedTimeAmongChunk<T extends { material: ReadonlyArray<unknown> }>(
  chunk: ReadonlyArray<T>,
  getElapsedMins: (c: T) => number,
  getActiveMins: (c: T) => number
): ReadonlyArray<LogEntryWithSplitElapsed<T>> {
  let totalActiveMins = 0;
  let totalMatCount = 0;
  let allEventsHaveActive = true;
  for (const cycle of chunk) {
    if (getActiveMins(cycle) < 0) {
      allEventsHaveActive = false;
    }
    totalMatCount += cycle.material.length;
    totalActiveMins += getActiveMins(cycle);
  }

  if (allEventsHaveActive && totalActiveMins > 0) {
    //split by active.  First multiply by (active/totalActive) ratio to get fraction of elapsed
    //for this cycle, then by material count to get per-material
    return chunk.map((cycle) => ({
      cycle,
      elapsedForSingleMaterialMinutes:
        (getElapsedMins(cycle) * getActiveMins(cycle)) / totalActiveMins / cycle.material.length,
    }));
  }

  // split equally among all material
  if (totalMatCount > 0) {
    return chunk.map((cycle) => ({
      cycle,
      elapsedForSingleMaterialMinutes: getElapsedMins(cycle) / totalMatCount,
    }));
  }

  // only when no events have material, which should never happen
  return chunk.map((cycle) => ({
    cycle,
    elapsedForSingleMaterialMinutes: getElapsedMins(cycle),
  }));
}

export function splitElapsedLoadTime<T extends { material: ReadonlyArray<unknown> }>(
  cycles: LazySeq<T>,
  getLuL: (c: T) => number,
  getTime: (c: T) => Date,
  getElapsedMins: (c: T) => number,
  getActiveMins: (c: T) => number
): LazySeq<LogEntryWithSplitElapsed<T>> {
  const loadEventsByLUL = cycles.groupBy(getLuL).mapValues((cs) => chunkCyclesWithSimilarEndTime(cs, getTime));

  return LazySeq.ofIterable(loadEventsByLUL.valueIterable())
    .flatMap((cycles) => cycles)
    .map((cs) => splitElapsedTimeAmongChunk(cs, getElapsedMins, getActiveMins))
    .flatMap((chunk) => chunk);
}

export function activeMinutes(cycle: Readonly<ILogEntry>, stats: StatisticalCycleTime | null): number {
  const aMins = durationToMinutes(cycle.active);
  if (cycle.active === "" || aMins <= 0 || cycle.material.length === 0) {
    return (stats?.expectedCycleMinutesForSingleMat ?? 0) * cycle.material.length;
  } else {
    return aMins;
  }
}

function estimateCycleTimesOfParts(cycles: Iterable<Readonly<ILogEntry>>): EstimatedCycleTimes {
  const machines = LazySeq.ofIterable(cycles)
    .filter((c) => c.type === LogType.MachineCycle && !c.startofcycle && c.material.length > 0)
    .groupBy((c) => PartAndStationOperation.ofLogCycle(c))
    .mapValues((cyclesForPartAndStat) =>
      estimateCycleTimes(cyclesForPartAndStat.map((cycle) => durationToMinutes(cycle.elapsed) / cycle.material.length))
    );

  const loads = splitElapsedLoadTime(
    LazySeq.ofIterable(cycles).filter(
      (c) => c.type === LogType.LoadUnloadCycle && !c.startofcycle && c.material.length > 0
    ),
    (c) => c.locnum,
    (c) => c.endUTC,
    (c) => durationToMinutes(c.elapsed),
    (c) => (c.active === "" ? -1 : durationToMinutes(c.active))
  )
    .groupBy((c) => PartAndStationOperation.ofLogCycle(c.cycle))
    .mapValues((cyclesForPartAndStat) =>
      estimateCycleTimes(cyclesForPartAndStat.map((c) => c.elapsedForSingleMaterialMinutes))
    );

  return machines.mergeWith(loads, (s1, _) => s1);
}

export const setLast30EstimatedCycleTimes = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(last30EstimatedTimesRW, estimateCycleTimesOfParts(log));
  }
);

export const setSpecificMonthEstimatedCycleTimes = conduit<ReadonlyArray<Readonly<ILogEntry>>>(
  (t: TransactionInterface_UNSTABLE, log: ReadonlyArray<Readonly<ILogEntry>>) => {
    t.set(specificMonthEstimatedTimesRW, estimateCycleTimesOfParts(log));
  }
);
