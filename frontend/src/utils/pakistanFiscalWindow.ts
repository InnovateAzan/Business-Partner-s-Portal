export function pakistanFiscalWindowStart(
  now = new Date()
) {
  const fiscalYearStart =
    now.getMonth() >= 6
      ? now.getFullYear()
      : now.getFullYear() - 1;

  return new Date(
    fiscalYearStart,
    3,
    1
  );
}

export function formatPakistanFiscalWindowStart(
  now = new Date()
) {
  return pakistanFiscalWindowStart(now)
    .toLocaleDateString(
      "en-GB",
      {
        day: "2-digit",
        month: "short",
        year: "numeric",
      }
    );
}

export function isInPakistanFiscalWindow(
  value: string | Date | null | undefined,
  now = new Date()
) {
  if (!value) return false;

  const date = value instanceof Date
    ? value
    : new Date(value);

  return !Number.isNaN(date.getTime()) &&
    date >= pakistanFiscalWindowStart(now);
}
