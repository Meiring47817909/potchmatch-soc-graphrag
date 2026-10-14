import { Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';
import { SocEvent } from '../soc-models';
import { shortId } from '../short-id';

// The 13 event fields from the specification, in spec order and with spec names as headers.
// Shared by the Events tab and the expanded "related events" of an alert.
@Component({
  selector: 'app-events-table',
  imports: [DatePipe],
  templateUrl: './events-table.html',
  styleUrl: './events-table.css',
})
export class EventsTable {
  readonly events = input.required<SocEvent[]>();
  readonly emptyText = input('No events.');
  protected readonly shortId = shortId;
}
