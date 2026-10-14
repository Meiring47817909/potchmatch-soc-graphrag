// First 8 characters of a GUID for table cells; the full value goes in the cell's title.
export function shortId(id: string | null | undefined): string {
  if (!id) return '-';
  return id.length > 8 ? `${id.slice(0, 8)}…` : id;
}
