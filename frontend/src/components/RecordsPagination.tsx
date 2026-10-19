import { useEffect, useState } from "react";

export const RECORDS_PER_PAGE = 13;

/** Keep paging consistent across record-list screens, without truncating exports. */
export function useRecordsPagination(total: number, resetKeys: readonly unknown[] = []) {
  const [page, setPage] = useState(1);
  const pages = Math.max(1, Math.ceil(total / RECORDS_PER_PAGE));
  const safePage = Math.min(page, pages);
  useEffect(() => { setPage(1); }, resetKeys);
  return {
    page: safePage,
    pages,
    setPage,
    start: (safePage - 1) * RECORDS_PER_PAGE,
    end: safePage * RECORDS_PER_PAGE,
  };
}

export function RecordsPagination({ total, page, pages, onPage, noun = "records" }: {
  total: number;
  page: number;
  pages: number;
  onPage: (page: number) => void;
  noun?: string;
}) {
  return <div className="po-pagination" style={{ display: "flex", alignItems: "center", justifyContent: "space-between", gap: 12, flexWrap: "wrap", marginTop: 16 }}>
    <span>{total} {noun}</span>
    <div style={{ display: "flex", alignItems: "center", gap: 10 }}>
      <button type="button" className="table-btn" disabled={page <= 1} onClick={() => onPage(page - 1)}>Previous</button>
      <span>Page {page} of {pages}</span>
      <button type="button" className="table-btn" disabled={page >= pages} onClick={() => onPage(page + 1)}>Next</button>
    </div>
  </div>;
}
