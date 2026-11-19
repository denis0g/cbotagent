import { Component, inject } from '@angular/core';
import { ActivatedRoute } from '@angular/router';

@Component({
  templateUrl: './feature.html',
})
export class Feature {
  private readonly route = inject(ActivatedRoute);
  readonly title = this.route.snapshot.data['title'] as string;
  readonly description = this.route.snapshot.data['description'] as string;
}
