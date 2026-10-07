import { TestBed } from '@angular/core/testing';

import { MockData } from './mock-data';

describe('MockData', () => {
  let service: MockData;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(MockData);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
