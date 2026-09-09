import { useEffect, useRef, useState } from "react";

type StatusOption = {
  value: string;
  label: string;
};

type Props = {
  searchValue: string;
  onSearchChange: (value: string) => void;
  searchPlaceholder: string;
  statusValue: string;
  onStatusChange: (value: string) => void;
  statusLabel?: string;
  statusOptions: StatusOption[];
  showDateFilter?: boolean;
  fromDate?: string;
  toDate?: string;
  onApplyDateRange?: (fromDate: string, toDate: string) => void;
  onExport: () => void;
};

export function DataTableToolbar({
  searchValue,
  onSearchChange,
  searchPlaceholder,
  statusValue,
  onStatusChange,
  statusLabel = "Status",
  statusOptions,
  showDateFilter = false,
  fromDate = "",
  toDate = "",
  onApplyDateRange,
  onExport,
}: Props) {
  const [dateOpen, setDateOpen] = useState(false);
  const [draftFrom, setDraftFrom] = useState(fromDate);
  const [draftTo, setDraftTo] = useState(toDate);
  const dateRef = useRef<HTMLDivElement | null>(null);

  useEffect(() => {
    setDraftFrom(fromDate);
    setDraftTo(toDate);
  }, [fromDate, toDate]);

  useEffect(() => {
    const handleOutside = (event: MouseEvent) => {
      if (
        dateRef.current &&
        !dateRef.current.contains(event.target as Node)
      ) {
        setDateOpen(false);
      }
    };

    document.addEventListener("mousedown", handleOutside);
    return () => document.removeEventListener("mousedown", handleOutside);
  }, []);

  const hasDateFilter = Boolean(fromDate || toDate);

  return (
    <div className="data-toolbar">
      <input
        className="data-toolbar-search"
        type="search"
        value={searchValue}
        onChange={(event) => onSearchChange(event.target.value)}
        placeholder={searchPlaceholder}
        aria-label={searchPlaceholder}
      />

      <select
        className="data-toolbar-select"
        value={statusValue}
        onChange={(event) => onStatusChange(event.target.value)}
        aria-label={`${statusLabel} filter`}
      >
        <option value="">{statusLabel}: All</option>
        {statusOptions.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>

      {showDateFilter && (
        <div className="date-filter-wrap" ref={dateRef}>
          <button
            type="button"
            className={`data-toolbar-btn ${hasDateFilter ? "active" : ""}`}
            onClick={() => setDateOpen((current) => !current)}
          >
            Date Filter
          </button>

          {dateOpen && (
            <div className="date-filter-popup">
              <label>
                From Date
                <input
                  type="date"
                  value={draftFrom}
                  onChange={(event) => setDraftFrom(event.target.value)}
                />
              </label>

              <label>
                To Date
                <input
                  type="date"
                  value={draftTo}
                  onChange={(event) => setDraftTo(event.target.value)}
                />
              </label>

              <div className="date-filter-actions">
                <button
                  type="button"
                  className="secondary-btn"
                  onClick={() => {
                    setDraftFrom("");
                    setDraftTo("");
                    onApplyDateRange?.("", "");
                    setDateOpen(false);
                  }}
                >
                  Clear
                </button>

                <button
                  type="button"
                  className="primary-btn"
                  onClick={() => {
                    onApplyDateRange?.(draftFrom, draftTo);
                    setDateOpen(false);
                  }}
                >
                  Apply
                </button>
              </div>
            </div>
          )}
        </div>
      )}

      <button
        type="button"
        className="data-toolbar-btn export-btn"
        onClick={onExport}
      >
        Export
      </button>
    </div>
  );
}
