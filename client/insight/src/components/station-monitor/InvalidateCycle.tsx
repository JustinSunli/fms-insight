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

import { Button } from "@mui/material";
import { MenuItem } from "@mui/material";
import { TextField } from "@mui/material";
import { Vector } from "prelude-ts";
import * as React from "react";
import { ICurrentStatus, IActiveJob, IInProcessMaterial, ILogEntry, LocType } from "../../network/api";
import { JobsBackend } from "../../network/backend";
import { LazySeq } from "../../util/lazyseq";

interface InvalidateCycle {
  readonly process: number | null;
  readonly updating: boolean;
}

export type InvalidateCycleState = InvalidateCycle | null;

export interface InvalidateDialogContentProps {
  readonly events: Vector<Readonly<ILogEntry>>;
  readonly st: InvalidateCycleState;
  readonly setState: (s: InvalidateCycleState) => void;
}

export function InvalidateCycleDialogContent(props: InvalidateDialogContentProps) {
  if (props.st === null) return <div />;

  const maxProc = LazySeq.ofIterable(props.events)
    .flatMap((e) => e.material)
    .maxOn((m) => m.proc)
    .map((m) => m.proc)
    .getOrElse(1);

  return (
    <div style={{ margin: "2em" }}>
      <p>
        An invalidated cycle remains in the event log, but is not considered when determining the next process to be
        machined on a piece of material.
      </p>
      <TextField
        value={props.st?.process ?? ""}
        select
        onChange={(e) =>
          props.st &&
          props.setState({
            ...props.st,
            process: parseInt(e.target.value),
          })
        }
        style={{ width: "20em" }}
        variant="outlined"
        label="Select process to invalidate"
      >
        {LazySeq.ofRange(1, maxProc + 1).map((p) => (
          <MenuItem key={p} value={p}>
            {p}
          </MenuItem>
        ))}
      </TextField>
    </div>
  );
}

export interface InvalidateCycleDialogButtonsProps {
  readonly curMat: Readonly<IInProcessMaterial> | null;
  readonly st: InvalidateCycleState;
  readonly operator: string | null;
  readonly setState: (s: InvalidateCycleState) => void;
  readonly close: () => void;
}

export function InvalidateCycleDialogButtons(props: InvalidateCycleDialogButtonsProps) {
  function invalidateCycle() {
    if (props.curMat && props.st && props.st.process) {
      props.setState({ ...props.st, updating: true });
      JobsBackend.invalidatePalletCycle(props.curMat.materialID, null, props.operator, props.st.process).finally(() =>
        props.close()
      );
    }
  }

  return (
    <>
      {props.curMat && props.st === null ? (
        <Button color="primary" onClick={() => props.setState({ process: null, updating: false })}>
          Invalidate Cycle
        </Button>
      ) : undefined}
      {props.curMat && props.st !== null ? (
        <Button color="primary" onClick={invalidateCycle} disabled={props.st.process === null || props.st.updating}>
          {props.st.process === null ? "Invalidate Cycle" : "Invalidate Process " + props.st.process.toString()}
        </Button>
      ) : undefined}
    </>
  );
}

// ----------------------------------------------------------------------------------
// Swap
// ----------------------------------------------------------------------------------

interface SwapMaterial {
  readonly selectedMatToSwap: Readonly<IInProcessMaterial> | null;
  readonly updating: boolean;
}

export type SwapMaterialState = SwapMaterial | null;

export interface SwapMaterialDialogContentProps {
  readonly curMat: Readonly<IInProcessMaterial> | null;
  readonly status: Readonly<ICurrentStatus>;
  readonly st: SwapMaterialState;
  readonly setState: (s: SwapMaterialState) => void;
}

function isNullOrEmpty(s: string | null | undefined): boolean {
  return s === undefined || s === null || s == "";
}

function matCanSwap(
  curMat: Readonly<IInProcessMaterial>,
  job: Readonly<IActiveJob> | undefined
): (m: Readonly<IInProcessMaterial>) => boolean {
  return (newMat) => {
    if (isNullOrEmpty(newMat.serial)) return false;
    if (newMat.location.type === LocType.OnPallet) return false;
    if (newMat.process !== curMat.process - 1) return false;
    if (isNullOrEmpty(newMat.jobUnique)) {
      // if part name is wrong, check casting
      if (isNullOrEmpty(newMat.partName)) return false;
      if (newMat.partName !== curMat.partName) {
        if (!job) return false;
        if (
          !LazySeq.ofIterable(job.procsAndPaths)
            .flatMap((p) => p.paths)
            .anyMatch((p) => p.casting === newMat.partName)
        ) {
          return false;
        }
      }
    } else {
      // check path
      if (newMat.jobUnique !== curMat.jobUnique) return false;
      if (newMat.path !== curMat.path) return false;
    }
    return true;
  };
}

export function SwapMaterialDialogContent(props: SwapMaterialDialogContentProps): JSX.Element {
  const curMat = props.curMat;
  if (curMat === null || props.st === null) return <div />;
  const curMatJob = props.status.jobs[curMat.jobUnique];

  const availMats = props.status.material.filter(matCanSwap(curMat, curMatJob));
  if (availMats.length === 0) {
    return (
      <p style={{ margin: "2em" }}>
        No material with the same job is available for swapping. You must edit the pallet using the cell controller
        software to remove the material from the pallet. Insight will automatically refresh once the cell controller
        software is updated.
      </p>
    );
  } else {
    return (
      <div style={{ margin: "2em" }}>
        <p>Swap serial on pallet with material from the same job.</p>
        <p>
          If material on the pallet is from a different job, you cannot use this screen. Instead, the material must
          first be removed from the pallet using the cell controller software. Insight will automatically refresh when
          this occurs.
        </p>
        <TextField
          value={props.st?.selectedMatToSwap?.serial ?? ""}
          select
          onChange={(e) =>
            props.st &&
            props.setState({
              ...props.st,
              selectedMatToSwap: availMats.find((m) => m.serial === e.target.value) ?? null,
            })
          }
          style={{ width: "20em" }}
          variant="outlined"
          label={"Select serial to swap with " + curMat.serial}
        >
          {availMats.map((m) => (
            <MenuItem key={m.materialID} value={m.serial}>
              {m.serial}
            </MenuItem>
          ))}
        </TextField>
      </div>
    );
  }
}

export interface SwapMaterialButtonsProps {
  readonly curMat: Readonly<IInProcessMaterial> | null;
  readonly st: SwapMaterialState;
  readonly operator: string | null;
  readonly setState: (s: SwapMaterialState) => void;
  readonly close: () => void;
}

export function SwapMaterialButtons(props: SwapMaterialButtonsProps) {
  function swapMats() {
    if (props.curMat && props.st && props.st.selectedMatToSwap && props.curMat.location.type === LocType.OnPallet) {
      props.setState({ selectedMatToSwap: props.st.selectedMatToSwap, updating: true });
      JobsBackend.swapMaterialOnPallet(props.curMat.materialID, props.operator, {
        pallet: props.curMat.location.pallet ?? "",
        materialIDToSetOnPallet: props.st.selectedMatToSwap.materialID,
      }).finally(() => props.close());
    }
  }

  return (
    <>
      {props.curMat && props.st === null && props.curMat.location.type === LocType.OnPallet ? (
        <Button color="primary" onClick={() => props.setState({ selectedMatToSwap: null, updating: false })}>
          Swap Serial
        </Button>
      ) : undefined}
      {props.curMat && props.st !== null && props.curMat.location.type === LocType.OnPallet ? (
        <Button color="primary" onClick={swapMats} disabled={props.st.selectedMatToSwap === null || props.st.updating}>
          {props.st.selectedMatToSwap === null ? "Swap Serial" : "Swap with " + props.st.selectedMatToSwap.serial}
        </Button>
      ) : undefined}
    </>
  );
}
