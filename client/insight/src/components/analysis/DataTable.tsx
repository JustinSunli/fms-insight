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
import * as React from "react";
import { Dialog, DialogActions, DialogContent, DialogTitle, styled, TableBody } from "@mui/material";
import { TableCell } from "@mui/material";
import { TableHead } from "@mui/material";
import { TableRow } from "@mui/material";
import { TableSortLabel } from "@mui/material";
import { Tooltip } from "@mui/material";
import { IconButton } from "@mui/material";
import FirstPageIcon from "@mui/icons-material/FirstPage";
import KeyboardArrowLeft from "@mui/icons-material/KeyboardArrowLeft";
import KeyboardArrowRight from "@mui/icons-material/KeyboardArrowRight";
import LastPageIcon from "@mui/icons-material/LastPage";
import { Toolbar } from "@mui/material";
import { Typography } from "@mui/material";
import { Select } from "@mui/material";
import ZoomOutIcon from "@mui/icons-material/ZoomOut";
import ZoomInIcon from "@mui/icons-material/ZoomIn";
import { InputBase } from "@mui/material";
import SkipPrevIcon from "@mui/icons-material/SkipPrevious";
import SkipNextIcon from "@mui/icons-material/SkipNext";
import { MenuItem } from "@mui/material";
import { Button } from "@mui/material";
import MoreHoriz from "@mui/icons-material/MoreHoriz";
import Calendar from "react-calendar";

import { addDays } from "date-fns";
import { LazySeq } from "../../util/lazyseq";
import { ToOrderable } from "prelude-ts";

export interface Column<Id, Row> {
  readonly id: Id;
  readonly numeric: boolean;
  readonly label: string;
  readonly getDisplay: (c: Row) => string;
  readonly getForSort?: ToOrderable<Row>;
}

export interface DataTableHeadProps<Id, Row> {
  readonly orderBy: Id;
  readonly order: "asc" | "desc";
  readonly columns: ReadonlyArray<Column<Id, Row>>;
  readonly onRequestSort: (id: Id) => void;
  readonly showDetailsCol: boolean;
}

export function DataTableHead<Id extends string | number, Row>(props: DataTableHeadProps<Id, Row>): JSX.Element {
  return (
    <TableHead>
      <TableRow>
        {props.columns.map((col) => (
          <TableCell
            key={col.id}
            align={col.numeric ? "right" : "left"}
            sortDirection={props.orderBy === col.id ? props.order : false}
          >
            <Tooltip title="Sort" placement={col.numeric ? "bottom-end" : "bottom-start"} enterDelay={300}>
              <TableSortLabel
                active={props.orderBy === col.id}
                direction={props.order}
                onClick={() => props.onRequestSort(col.id)}
              >
                {col.label}
              </TableSortLabel>
            </Tooltip>
          </TableCell>
        ))}
        {props.showDetailsCol ? <TableCell padding="checkbox" /> : undefined}
      </TableRow>
    </TableHead>
  );
}

export enum DataTableActionZoomType {
  Last30Days = "Last30",
  ZoomIntoRange = "IntoRange",
  ExtendDays = "Extend",
}

interface DataTableActionZoomIntoRange {
  readonly type: DataTableActionZoomType.ZoomIntoRange;
  readonly default_date_range: Date[];
  readonly current_date_zoom: { start: Date; end: Date } | undefined;
  readonly set_date_zoom_range: (p: { start: Date; end: Date } | undefined) => void;
}

export type DataTableActionZoom =
  | {
      readonly type: DataTableActionZoomType.Last30Days;
      readonly set_days_back: (numDaysBack: number | null) => void;
    }
  | DataTableActionZoomIntoRange
  | {
      readonly type: DataTableActionZoomType.ExtendDays;
      readonly curStart: Date;
      readonly curEnd: Date;
      readonly extend: (numDays: number) => void;
    };

interface SelectDateRangeProps {
  readonly zoom: DataTableActionZoomIntoRange;
}

const dateFormat = new Intl.DateTimeFormat([], { year: "numeric", month: "short", day: "numeric" });
const monthFormat = new Intl.DateTimeFormat([], { year: "numeric", month: "long" });

const StyledCalendar = styled(Calendar)(({ theme }) => ({
  width: "350px",
  "& .react-calendar__month-view__weekdays": {
    textAlign: "center",
    textTransform: "uppercase",
    fontWeight: "bold",
  },
  "& .react-calendar__tile": {
    textAlign: "center",
    padding: ".75em .5em",
    margin: 0,
    border: 0,
    background: "none",
    outline: "none",
    cursor: "pointer",
    "&:hover": {
      backgroundColor: "rgb(230, 230, 230)",
    },
    "&--active": {
      backgroundColor: theme.palette.primary.light,
      "&:hover": {
        backgroundColor: theme.palette.primary.dark,
      },
    },
  },
  // react-calendar__tile--hover is when selecting range of days.
  "&.react-calendar--selectRange .react-calendar__tile--hover": {
    backgroundColor: "rgb(230, 230, 230)",
  },
}));

function SelectDateRange(props: SelectDateRangeProps) {
  const [open, setOpen] = React.useState(false);
  const start = props.zoom.current_date_zoom ? props.zoom.current_date_zoom.start : props.zoom.default_date_range[0];
  const end = addDays(
    props.zoom.current_date_zoom ? props.zoom.current_date_zoom.end : props.zoom.default_date_range[1],
    -1
  );

  function onChange(d: ReadonlyArray<Date>) {
    props.zoom.set_date_zoom_range({
      start: d[0],
      end: addDays(d[1], 1),
    });
    setOpen(false);
  }

  // @types/react-calendar has the wrong type for onChange
  // eslint-disable-next-line @typescript-eslint/no-unsafe-assignment, @typescript-eslint/no-explicit-any
  const matchingOnChange: (d: Date) => void = onChange as any;

  return (
    <>
      <span>
        {dateFormat.format(props.zoom.current_date_zoom?.start ?? props.zoom.default_date_range[0])} -{" "}
        {dateFormat.format(props.zoom.current_date_zoom?.end ?? props.zoom.default_date_range[1])}
      </span>
      <Tooltip title="Zoom To Date Range">
        <IconButton onClick={() => setOpen(true)} size="large">
          <ZoomInIcon />
        </IconButton>
      </Tooltip>
      <Tooltip title="Reset Date Range">
        <IconButton onClick={() => props.zoom.set_date_zoom_range(undefined)} size="large">
          <ZoomOutIcon />
        </IconButton>
      </Tooltip>
      <Dialog open={open} onClose={() => setOpen(false)}>
        <DialogTitle>Select Date Range {monthFormat.format(props.zoom.default_date_range[0])}</DialogTitle>
        <DialogContent>
          <StyledCalendar
            minDate={props.zoom.default_date_range[0]}
            maxDate={props.zoom.default_date_range[1]}
            calendarType="US"
            selectRange
            showNavigation={false}
            showNeighboringMonth={false}
            value={[start, end]}
            onChange={matchingOnChange}
          />
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>Close</Button>
        </DialogActions>
      </Dialog>
    </>
  );
}

export interface DataTableActionsProps {
  readonly page: number;
  readonly count: number;
  readonly rowsPerPage: number;
  readonly setPage: (page: number) => void;
  readonly setRowsPerPage: (rpp: number) => void;
  readonly zoom?: DataTableActionZoom;
}

export function DataTableActions(props: DataTableActionsProps): JSX.Element {
  const zoom = props.zoom;

  let zoomCtrl;
  if (zoom && zoom.type === DataTableActionZoomType.Last30Days) {
    zoomCtrl = (
      <>
        <Tooltip title="Last 24 hours">
          <Button onClick={() => zoom.set_days_back(1)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            24h
          </Button>
        </Tooltip>
        <Tooltip title="Last 2 days">
          <Button onClick={() => zoom.set_days_back(2)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            2d
          </Button>
        </Tooltip>
        <Tooltip title="Last 3 days">
          <Button onClick={() => zoom.set_days_back(3)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            3d
          </Button>
        </Tooltip>
        <Tooltip title="Last 4 days">
          <Button onClick={() => zoom.set_days_back(4)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            4d
          </Button>
        </Tooltip>
        <Tooltip title="Last 5 days">
          <Button onClick={() => zoom.set_days_back(5)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            5d
          </Button>
        </Tooltip>
        <Tooltip title="Last 6 days">
          <Button onClick={() => zoom.set_days_back(6)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            6d
          </Button>
        </Tooltip>
        <Tooltip title="Last 1 week">
          <Button onClick={() => zoom.set_days_back(7)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            1w
          </Button>
        </Tooltip>
        <Tooltip title="Last 2 weeks">
          <Button onClick={() => zoom.set_days_back(7 * 2)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            2w
          </Button>
        </Tooltip>
        <Tooltip title="Last 3 weeks">
          <Button onClick={() => zoom.set_days_back(7 * 3)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            3w
          </Button>
        </Tooltip>
        <Tooltip title="Last 30 days">
          <Button onClick={() => zoom.set_days_back(null)} style={{ color: "rgba(0, 0, 0, 0.54)" }}>
            30d
          </Button>
        </Tooltip>
      </>
    );
  } else if (zoom && zoom.type === DataTableActionZoomType.ZoomIntoRange) {
    zoomCtrl = <SelectDateRange zoom={zoom} />;
  } else if (zoom && zoom.type === DataTableActionZoomType.ExtendDays) {
    zoomCtrl = (
      <>
        <Tooltip title="Extend 1 day previous">
          <IconButton onClick={() => zoom.extend(-1)} size="large">
            <SkipPrevIcon />
          </IconButton>
        </Tooltip>
        <Typography variant="caption">
          {zoom.curStart.toLocaleDateString() + " to " + zoom.curEnd.toLocaleDateString()}
        </Typography>
        <Tooltip title="Extend 1 day">
          <IconButton onClick={() => zoom.extend(1)} size="large">
            <SkipNextIcon />
          </IconButton>
        </Tooltip>
      </>
    );
  }

  return (
    <Toolbar>
      <Typography color="textSecondary" variant="caption">
        Rows per page:
      </Typography>
      <Select
        style={{ marginLeft: 8, marginRight: "1em" }}
        value={props.rowsPerPage}
        SelectDisplayProps={{ style: { color: "rgba(0, 0, 0, 0.54)" } }}
        input={<InputBase />}
        onChange={(evt) => {
          const rpp = parseInt(evt.target.value as string, 10);
          props.setRowsPerPage(rpp);
          const maxPage = Math.ceil(props.count / rpp) - 1;
          if (props.page > maxPage) {
            props.setPage(maxPage);
          }
        }}
      >
        {[10, 15, 20, 50].map((rowsPerPageOption) => (
          <MenuItem key={rowsPerPageOption} value={rowsPerPageOption}>
            {rowsPerPageOption}
          </MenuItem>
        ))}
      </Select>
      <Typography color="textSecondary" variant="caption">
        {`${props.count === 0 ? 0 : props.page * props.rowsPerPage + 1}-${Math.min(
          props.count,
          (props.page + 1) * props.rowsPerPage
        )} of ${props.count}`}
      </Typography>
      <IconButton onClick={() => props.setPage(0)} disabled={props.page === 0} aria-label="First Page" size="large">
        <FirstPageIcon />
      </IconButton>
      <IconButton
        onClick={() => props.setPage(props.page - 1)}
        disabled={props.page === 0}
        aria-label="Previous Page"
        size="large"
      >
        <KeyboardArrowLeft />
      </IconButton>
      <IconButton
        onClick={() => props.setPage(props.page + 1)}
        disabled={props.page >= Math.ceil(props.count / props.rowsPerPage) - 1}
        aria-label="Next Page"
        size="large"
      >
        <KeyboardArrowRight />
      </IconButton>
      <IconButton
        onClick={() => props.setPage(Math.max(0, Math.ceil(props.count / props.rowsPerPage) - 1))}
        disabled={props.page >= Math.ceil(props.count / props.rowsPerPage) - 1}
        aria-label="Last Page"
        size="large"
      >
        <LastPageIcon />
      </IconButton>
      {zoom ? (
        <>
          <div style={{ flexGrow: 1 }} />
          {zoomCtrl}
        </>
      ) : undefined}
    </Toolbar>
  );
}

export interface DataTableBodyProps<Id, Row> {
  readonly pageData: Iterable<Row>;
  readonly columns: ReadonlyArray<Column<Id, Row>>;
  readonly onClickDetails?: (event: React.MouseEvent, r: Row) => void;
}

export class DataTableBody<Id extends string | number, Row> extends React.PureComponent<DataTableBodyProps<Id, Row>> {
  render(): JSX.Element {
    const onClickDetails = this.props.onClickDetails;
    return (
      <TableBody>
        {LazySeq.ofIterable(this.props.pageData).map((row, idx) => (
          <TableRow key={idx}>
            {this.props.columns.map((col) => (
              <TableCell key={col.id} align={col.numeric ? "right" : "left"}>
                {col.getDisplay(row)}
              </TableCell>
            ))}
            {onClickDetails ? (
              <TableCell padding="checkbox">
                <IconButton onClick={(e) => onClickDetails(e, row)} size="large">
                  <MoreHoriz />
                </IconButton>
              </TableCell>
            ) : undefined}
          </TableRow>
        ))}
      </TableBody>
    );
  }
}
