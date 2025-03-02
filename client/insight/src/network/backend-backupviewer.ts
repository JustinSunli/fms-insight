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

import * as api from "./api";
import { RouteLocation } from "../components/routes";
import { registerBackend } from "./backend";
import { onLoadLast30Jobs, onLoadLast30Log } from "../cell-status/loading";
import { atom, RecoilState, RecoilValueReadOnly, useRecoilCallback } from "recoil";
import { RecoilConduit } from "../util/recoil-util";
import { addDays } from "date-fns";

type Request = {
  name: string;
  id: number;
  payload: any;
};

type Response = {
  id: number;
  response?: any;
  error?: string;
};

const inFlight = new Map<number, (response: Response) => void>();
let lastId = 0;
let port: MessagePort | null = null;
let msgHandlerRegsitered = false;

const loadingBackupViewerRW = atom<boolean>({ key: "loading-backup-viewer-data", default: false });
export const loadingBackupViewer: RecoilValueReadOnly<boolean> = loadingBackupViewerRW;

const errorLoadingBackupViewerRW = atom<string | null>({ key: "error-backup-viewer-data", default: null });
export const errorLoadingBackupViewer: RecoilValueReadOnly<string | null> = errorLoadingBackupViewerRW;

function loadLast30(set: <T>(s: RecoilState<T>, t: T) => void, push: <T>(c: RecoilConduit<T>) => (t: T) => void): void {
  set(loadingBackupViewerRW, true);
  set(errorLoadingBackupViewerRW, null);

  const now = new Date();
  const thirtyDaysAgo = addDays(now, -30);

  const jobsProm = JobsBackend.history(thirtyDaysAgo, now).then(push(onLoadLast30Jobs));
  const logProm = LogBackend.get(thirtyDaysAgo, now).then(push(onLoadLast30Log));

  Promise.all([jobsProm, logProm])
    .catch((e: Record<string, string | undefined>) => set(errorLoadingBackupViewerRW, e.message ?? e.toString()))
    .finally(() => set(loadingBackupViewerRW, false));
}

export function useRequestOpenBackupFile(): () => void {
  return useRecoilCallback(
    ({ set, transact_UNSTABLE }) =>
      () => {
        function push<T>(c: RecoilConduit<T>): (t: T) => void {
          return (t) => transact_UNSTABLE((trans) => c.transform(trans, t));
        }

        function onWindowMessage(evt: MessageEvent<unknown>) {
          if (evt.source === window && evt.data === "insight-file-opened") {
            port = evt.ports[0];
            port.onmessage = (msg) => {
              // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment
              const response: Response = msg.data;
              const handler = inFlight.get(response.id);
              if (handler) {
                handler(response);
              }
            };
            window.removeEventListener("message", onWindowMessage);
            window.history.pushState(null, "", RouteLocation.Backup_Efficiency);

            loadLast30(set, push);
          }
        }

        if (msgHandlerRegsitered === false) {
          window.addEventListener("message", onWindowMessage);
          msgHandlerRegsitered = true;
        }

        window.postMessage("open-insight-file", "*");
      },
    []
  );
}

export function registerBackupViewerBackend(): void {
  registerBackend(LogBackend, JobsBackend, ServerBackend, MachineBackend);
}

function sendIpc<P, R>(name: string, payload: P): Promise<R> {
  if (port === null) throw "No background port";

  const messageId = lastId;
  lastId += 1;
  const req: Request = {
    name,
    payload,
    id: messageId,
  };
  return new Promise((resolve, reject) => {
    inFlight.set(messageId, (response) => {
      inFlight.delete(messageId);
      if (response.error) {
        reject(response.error);
      } else {
        resolve(response.response);
      }
    });
    port?.postMessage(req);
  });
}

const ServerBackend = {
  fMSInformation(): Promise<api.IFMSInfo> {
    return Promise.resolve({
      name: "FMS Insight Backup Viewer",
      version: "",
      requireScanAtWash: false,
      requireWorkorderBeforeAllowWashComplete: false,
      additionalLogServers: [],
      usingLabelPrinterForSerials: false,
    });
  },
  printLabel(): Promise<void> {
    return Promise.resolve();
  },
};

const JobsBackend = {
  async history(startUTC: Date, endUTC: Date): Promise<Readonly<api.IHistoricData>> {
    const ret: {
      jobs: { [uniq: string]: object };
      stationUse: Array<object>;
    } = await sendIpc("job-history", {
      startUTC,
      endUTC,
    });
    const jobs: { [uniq: string]: api.HistoricJob } = {};
    for (const uniq of Object.keys(ret.jobs)) {
      jobs[uniq] = api.HistoricJob.fromJS(ret.jobs[uniq]);
    }
    return {
      jobs,
      stationUse: ret.stationUse.map(api.SimulatedStationUtilization.fromJS),
    };
  },
  currentStatus(): Promise<Readonly<api.ICurrentStatus>> {
    return Promise.resolve({
      jobs: {},
      pallets: {},
      material: [],
      alarms: [],
      queues: {},
      timeOfCurrentStatusUTC: new Date(),
    });
  },
  mostRecentUnfilledWorkordersForPart(): Promise<ReadonlyArray<Readonly<api.IPartWorkorder>>> {
    return Promise.resolve([]);
  },
  setJobComment(): Promise<void> {
    // do nothing
    return Promise.resolve();
  },

  removeMaterialFromAllQueues(): Promise<void> {
    // do nothing
    return Promise.resolve();
  },
  bulkRemoveMaterialFromQueues(): Promise<void> {
    // do nothing
    return Promise.resolve();
  },
  setMaterialInQueue(): Promise<void> {
    // do nothing
    return Promise.resolve();
  },
  addUnprocessedMaterialToQueue(): Promise<Readonly<api.IInProcessMaterial> | undefined> {
    // do nothing
    return Promise.resolve(undefined);
  },
  addUnallocatedCastingToQueue(): Promise<ReadonlyArray<Readonly<api.IInProcessMaterial>>> {
    // do nothing
    return Promise.resolve([]);
  },
  addUnallocatedCastingToQueueByPart(): Promise<Readonly<api.IInProcessMaterial> | undefined> {
    // do nothing
    return Promise.resolve(undefined);
  },
  signalMaterialForQuarantine(): Promise<void> {
    return Promise.resolve();
  },
  swapMaterialOnPallet(): Promise<void> {
    return Promise.resolve();
  },
  invalidatePalletCycle(): Promise<void> {
    return Promise.resolve();
  },
};

const LogBackend = {
  async get(startUTC: Date, endUTC: Date): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await sendIpc("log-get", {
      startUTC,
      endUTC,
    });
    return entries.map(api.LogEntry.fromJS);
  },

  recent(_lastSeenCounter: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    return Promise.reject("not implemented");
  },
  async logForMaterial(materialID: number): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await sendIpc("log-for-material", {
      materialID,
    });
    return entries.map(api.LogEntry.fromJS);
  },
  async logForMaterials(materialIDs: ReadonlyArray<number>): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await sendIpc("log-for-materials", {
      materialIDs,
    });
    return entries.map(api.LogEntry.fromJS);
  },
  async logForSerial(serial: string): Promise<ReadonlyArray<Readonly<api.ILogEntry>>> {
    const entries: ReadonlyArray<object> = await sendIpc("log-for-serial", {
      serial,
    });
    return entries.map(api.LogEntry.fromJS);
  },
  getWorkorders(): Promise<ReadonlyArray<Readonly<api.IWorkorderSummary>>> {
    return Promise.resolve([]);
  },

  setInspectionDecision(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  recordInspectionCompleted(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  recordWashCompleted(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },

  setWorkorder(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },
  setSerial(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },
  recordOperatorNotes(): Promise<Readonly<api.ILogEntry>> {
    return Promise.reject("Not implemented");
  },
};

const MachineBackend = {
  getToolsInMachines(): Promise<ReadonlyArray<Readonly<api.IToolInMachine>>> {
    return Promise.reject("Not implemented");
  },
  getProgramsInCellController(): Promise<ReadonlyArray<Readonly<api.IProgramInCellController>>> {
    return Promise.reject("Not implemented");
  },
  getProgramRevisionContent(): Promise<string> {
    return Promise.reject("Not implemented");
  },
  getLatestProgramRevisionContent(): Promise<string> {
    return Promise.reject("Not implemented");
  },
  getProgramRevisionsInDescendingOrderOfRevision(): Promise<ReadonlyArray<Readonly<api.IProgramRevision>>> {
    return Promise.reject("Not implemented");
  },
};
