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

import * as React from 'react';
import DocumentTitle from 'react-document-title';

import { MaterialSummary } from '../../data/events';
import { Store, connect, mkAC } from '../../store/store';
import { MaterialDialog, WhiteboardRegion, InProcMaterial } from './Material';
import * as matDetails from '../../data/material-details';
import { AllMaterialBins, selectAllMaterialIntoBins } from '../../data/all-material-bins';
import { createSelector } from 'reselect';

const ConnectedAllMatDialog = connect(
  st => ({
    display_material: st.MaterialDetails.material,
  }),
  {
    onClose: mkAC(matDetails.ActionType.CloseMaterialDialog),
  }
)(MaterialDialog);

export interface AllMatProps {
  readonly allMat: AllMaterialBins;
  readonly openMat: (mat: MaterialSummary) => void;
}

export function Wash(props: AllMatProps) {
  const regions = props.allMat.toSeq().sortBy((mats, region) => region);

  return (
    <DocumentTitle title="All Material - FMS Insight">
      <main style={{padding: '8px'}}>
        <div>
          {regions.map((mats, region) =>
            <WhiteboardRegion key={region} label={region} borderBottom flexStart>
              {mats.map((m, idx) =>
                <InProcMaterial key={idx} mat={m} onOpen={props.openMat} includePalletInAction/>
              )}
            </WhiteboardRegion>
          ).valueSeq()
          }
        </div>
        <ConnectedAllMatDialog/>
      </main>
    </DocumentTitle>
  );
}

export const extractMaterialRegions = createSelector(
  (st: Store) => st.Current.current_status,
  selectAllMaterialIntoBins
);

export default connect(
  (st: Store) => ({
    allMat: extractMaterialRegions(st)
  }),
  {
    openMat: matDetails.openMaterialDialog,
  }
)(Wash);