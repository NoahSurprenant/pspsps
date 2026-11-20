export interface WindowJson { starts: string; ends: string; yearly?: boolean }
export interface WindowFields { start: string; end: string; yearly: boolean }

const MONTHS = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

/** The window as the two dates (YYYY-MM-DD, UTC) and the yearly box of the picker. */
export function fieldsOf(window: WindowJson): WindowFields {
  return { start: window.starts.slice(0, 10), end: window.ends.slice(0, 10), yearly: window.yearly === true };
}

/** The window as the server keeps it: from the start of the first day up to the start of the last (not included). */
export function windowOf(fields: WindowFields): WindowJson {
  return { starts: `${fields.start}T00:00:00Z`, ends: `${fields.end}T00:00:00Z`, yearly: fields.yearly };
}

const monthDay = (date: string): number => Number(date.slice(5, 7)) * 100 + Number(date.slice(8, 10));

/** What is wrong with the dates, as the server will judge them; null when fine. */
export function windowProblem(fields: WindowFields): string | null {
  const valid = /^\d{4}-\d{2}-\d{2}$/;
  if (!valid.test(fields.start) || !valid.test(fields.end)) return "Pick both dates.";
  if (fields.yearly) return monthDay(fields.start) === monthDay(fields.end) ? "It starts and ends on the same day of the year." : null;
  return fields.end <= fields.start ? "It ends before it starts." : null;
}

/** Whether the window is on at a day (YYYY-MM-DD), the way the server decides it. */
export function onAt(fields: WindowFields, day: string): boolean {
  if (!fields.yearly) return day >= fields.start && day < fields.end;
  const from = monthDay(fields.start);
  const to = monthDay(fields.end);
  const today = monthDay(day);
  return from <= to ? today >= from && today < to : today >= from || today < to;
}

/** "1 Dec to 6 Jan, every year": the window said in words. */
export function summary(fields: WindowFields): string {
  const say = (date: string): string => `${Number(date.slice(8, 10))} ${MONTHS[Number(date.slice(5, 7)) - 1] ?? "?"}`;
  return fields.yearly
    ? `${say(fields.start)} to ${say(fields.end)}, every year`
    : `${say(fields.start)} ${fields.start.slice(0, 4)} to ${say(fields.end)} ${fields.end.slice(0, 4)}`;
}

/** For each month of a year, which days the window is on: the strip the picker draws. */
export function calendar(fields: WindowFields, year: number): boolean[][] {
  return MONTHS.map((_, month) => {
    const days = new Date(Date.UTC(year, month + 1, 0)).getUTCDate();
    return Array.from({ length: days }, (__, day) =>
      onAt(fields, `${year}-${String(month + 1).padStart(2, "0")}-${String(day + 1).padStart(2, "0")}`));
  });
}

export const monthNames = MONTHS;
