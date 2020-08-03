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
import * as React from "react";
import { IToolInMachine } from "../../data/api";
import { MachineBackend } from "../../data/backend";
import Fab from "@material-ui/core/Fab";
import CircularProgress from "@material-ui/core/CircularProgress";
import Card from "@material-ui/core/Card";
import CardContent from "@material-ui/core/CardContent";
import TimeAgo from "react-timeago";
import RefreshIcon from "@material-ui/icons/Refresh";
import CardHeader from "@material-ui/core/CardHeader";
import ToolIcon from "@material-ui/icons/Dns";
import Table from "@material-ui/core/Table";
import TableHead from "@material-ui/core/TableHead";
import TableCell from "@material-ui/core/TableCell";
import TableRow from "@material-ui/core/TableRow";
import TableSortLabel from "@material-ui/core/TableSortLabel";
import Tooltip from "@material-ui/core/Tooltip";
import { calcToolSummary, ToolReport } from "../../data/results.tools";
import TableBody from "@material-ui/core/TableBody";
import IconButton from "@material-ui/core/IconButton";
import KeyboardArrowDownIcon from "@material-ui/icons/KeyboardArrowDown";
import KeyboardArrowUpIcon from "@material-ui/icons/KeyboardArrowUp";
import Collapse from "@material-ui/core/Collapse";
import { LazySeq } from "../../data/lazyseq";
import { makeStyles } from "@material-ui/core/styles";

interface ToolRowProps {
  readonly tool: ToolReport;
}

const useRowStyles = makeStyles({
  mainRow: {
    "& > *": {
      borderBottom: "unset",
    },
  },
  collapseCell: {
    paddingBottom: 0,
    paddingTop: 0,
  },
  detailTable: {
    width: "auto",
    marginLeft: "10em",
    marginBottom: "1em",
  },
});

function ToolRow(props: ToolRowProps) {
  const [open, setOpen] = React.useState<boolean>(false);
  const classes = useRowStyles();

  return (
    <>
      <TableRow className={classes.mainRow}>
        <TableCell>
          <IconButton size="small" onClick={() => setOpen(!open)}>
            {open ? <KeyboardArrowUpIcon /> : <KeyboardArrowDownIcon />}
          </IconButton>
        </TableCell>
        <TableCell>{props.tool.toolName}</TableCell>
        <TableCell align="right">0</TableCell>
        <TableCell align="right">{props.tool.machines.sumOn((m) => m.remainingMinutes)}</TableCell>
        <TableCell align="right">{props.tool.minRemainingMinutes}</TableCell>
        <TableCell>{props.tool.minRemainingMachine}</TableCell>
      </TableRow>
      <TableRow>
        <TableCell className={classes.collapseCell} colSpan={6}>
          <Collapse in={open} timeout="auto" unmountOnExit>
            <Table size="small" className={classes.detailTable}>
              <TableHead>
                <TableRow>
                  <TableCell>Machine</TableCell>
                  <TableCell align="right">Pocket</TableCell>
                  <TableCell align="right">Current Use (min)</TableCell>
                  <TableCell align="right">Lifetime (min)</TableCell>
                  <TableCell align="right">Remaining Use (min)</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {LazySeq.ofIterable(props.tool.machines).map((m, idx) => (
                  <TableRow key={idx}>
                    <TableCell>{m.machineName}</TableCell>
                    <TableCell align="right">{m.pocket}</TableCell>
                    <TableCell align="right">{m.currentUseMinutes}</TableCell>
                    <TableCell align="right">{m.lifetimeMinutes}</TableCell>
                    <TableCell align="right">{m.remainingMinutes}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Collapse>
        </TableCell>
      </TableRow>
    </>
  );
}

interface ToolTableProps {
  readonly toolsInMach: ReadonlyArray<Readonly<IToolInMachine>>;
}

type SortColumn = "ToolName" | "ScheduledUse" | "RemainingTotalLife" | "MinRemainingLife" | "MinRemainingMachine";

function ToolSummaryTable(props: ToolTableProps) {
  const [sortCol, setSortCol] = React.useState<SortColumn>("ToolName");
  const [sortDir, setSortDir] = React.useState<"asc" | "desc">("asc");

  const report = React.useMemo(() => calcToolSummary(props.toolsInMach), [props.toolsInMach]);

  const rows = report.sortBy((a: ToolReport, b: ToolReport) => {
    let c: number = 0;
    switch (sortCol) {
      case "ToolName":
        c = a.toolName.localeCompare(b.toolName);
        break;
      case "ScheduledUse":
        c = 0;
        break;
      case "RemainingTotalLife":
        c = a.machines.sumOn((m) => m.remainingMinutes) - b.machines.sumOn((m) => m.remainingMinutes);
        break;
      case "MinRemainingLife":
        c = a.minRemainingMinutes - b.minRemainingMinutes;
        break;
      case "MinRemainingMachine":
        c = a.minRemainingMachine.localeCompare(b.minRemainingMachine);
        break;
    }
    if (c === 0) {
      return 0;
    } else if ((c < 0 && sortDir === "asc") || (c > 0 && sortDir === "desc")) {
      return -1;
    } else {
      return 1;
    }
  });

  function toggleSort(s: SortColumn) {
    if (s == sortCol) {
      setSortDir(sortDir === "asc" ? "desc" : "asc");
    } else {
      setSortCol(s);
    }
  }

  return (
    <Table>
      <TableHead>
        <TableRow>
          <TableCell />
          <TableCell sortDirection={sortCol === "ToolName" ? sortDir : false}>
            <TableSortLabel active={sortCol === "ToolName"} direction={sortDir} onClick={() => toggleSort("ToolName")}>
              Tool
            </TableSortLabel>
          </TableCell>
          <TableCell sortDirection={sortCol === "ScheduledUse" ? sortDir : false} align="right">
            <Tooltip title="Expected use for all currently scheduled parts">
              <TableSortLabel
                active={sortCol === "ScheduledUse"}
                direction={sortDir}
                onClick={() => toggleSort("ScheduledUse")}
              >
                Scheduled Use (min)
              </TableSortLabel>
            </Tooltip>
          </TableCell>
          <TableCell sortDirection={sortCol === "RemainingTotalLife" ? sortDir : false} align="right">
            <Tooltip title="Remaining life summed over all machines">
              <TableSortLabel
                active={sortCol === "RemainingTotalLife"}
                direction={sortDir}
                onClick={() => toggleSort("RemainingTotalLife")}
              >
                Total Remaining Life (min)
              </TableSortLabel>
            </Tooltip>
          </TableCell>
          <TableCell sortDirection={sortCol === "MinRemainingLife" ? sortDir : false} align="right">
            <Tooltip title="Machine with the least remaining life">
              <TableSortLabel
                active={sortCol === "MinRemainingLife"}
                direction={sortDir}
                onClick={() => toggleSort("MinRemainingLife")}
              >
                Smallest Remaining Life (min)
              </TableSortLabel>
            </Tooltip>
          </TableCell>
          <TableCell sortDirection={sortCol === "MinRemainingMachine" ? sortDir : false}>
            <Tooltip title="Machine with the least remaining life">
              <TableSortLabel
                active={sortCol === "MinRemainingMachine"}
                direction={sortDir}
                onClick={() => toggleSort("MinRemainingMachine")}
              >
                Machine With Smallest Remaining Life
              </TableSortLabel>
            </Tooltip>
          </TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((tool) => (
          <ToolRow key={tool.toolName} tool={tool} />
        ))}
      </TableBody>
    </Table>
  );
}

interface ToolNavHeaderProps {
  readonly refreshTime: Date | null;
  readonly loading: boolean;
  readonly loadTools: () => void;
}

function ToolNavHeader(props: ToolNavHeaderProps) {
  if (props.refreshTime === null) {
    return (
      <main style={{ margin: "2em", display: "flex", justifyContent: "center" }}>
        <Fab
          color="secondary"
          size="large"
          variant="extended"
          style={{ margin: "2em" }}
          onClick={props.loadTools}
          disabled={props.loading}
        >
          {props.loading ? (
            <>
              <CircularProgress size={10} style={{ marginRight: "1em" }} />
              Loading
            </>
          ) : (
            <>
              <RefreshIcon style={{ marginRight: "1em" }} />
              Load Tools
            </>
          )}
        </Fab>
      </main>
    );
  } else {
    return (
      <nav
        style={{
          display: "flex",
          backgroundColor: "#E0E0E0",
          paddingLeft: "24px",
          paddingRight: "24px",
          minHeight: "2.5em",
          alignItems: "center",
        }}
      >
        <Tooltip title="Refresh Tools">
          <div>
            <IconButton onClick={props.loadTools} disabled={props.loading} size="small">
              {props.loading ? <CircularProgress size={10} /> : <RefreshIcon fontSize="inherit" />}
            </IconButton>
          </div>
        </Tooltip>
        <span style={{ marginLeft: "1em" }}>
          Tools from <TimeAgo date={props.refreshTime} />
        </span>
      </nav>
    );
  }
}

export function ToolReport() {
  React.useEffect(() => {
    document.title = "Tool Report - FMS Insight";
  }, []);
  const [toolsInMach, setToolsInMach] = React.useState<ReadonlyArray<Readonly<IToolInMachine>> | null>(null);
  const [refreshTime, setRefreshTime] = React.useState<Date | null>(null);
  const [loading, setLoading] = React.useState<boolean>(false);
  const [error, setError] = React.useState<string | null>(null);

  const loadTools = React.useCallback(async () => {
    setLoading(true);
    setError(null);
    setToolsInMach([]);
    try {
      setToolsInMach(await MachineBackend.getToolsInMachines());
      setRefreshTime(new Date());
    } catch (e) {
      setError(e);
    } finally {
      setLoading(false);
    }
  }, [setLoading, setError, setToolsInMach, setRefreshTime]);

  return (
    <>
      <ToolNavHeader loading={loading} loadTools={loadTools} refreshTime={refreshTime} />
      <main style={{ padding: "24px" }}>
        {error != null ? (
          <Card style={{ margin: "3em" }}>
            <CardContent>{error}</CardContent>
          </Card>
        ) : undefined}
        {toolsInMach !== null ? (
          <Card style={{ margin: "3em" }} raised>
            <CardHeader
              title={
                <div style={{ display: "flex", flexWrap: "wrap", alignItems: "center" }}>
                  <ToolIcon style={{ color: "#6D4C41" }} />
                  <div style={{ marginLeft: "10px", marginRight: "3em" }}>Tools</div>
                </div>
              }
            />
            <CardContent>
              <ToolSummaryTable toolsInMach={toolsInMach} />
            </CardContent>
          </Card>
        ) : undefined}
      </main>
    </>
  );
}