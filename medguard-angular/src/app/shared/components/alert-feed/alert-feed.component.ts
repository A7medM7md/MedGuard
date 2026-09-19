import { Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Alert } from '../../../core/models';
import { alertSeverityMeta } from '../../../core/status';
import { timeAgo } from '../../../core/format';
import { StatusBadgeComponent } from '../status-badge/status-badge.component';

@Component({
  selector: 'mg-alert-feed',
  standalone: true,
  imports: [CommonModule, StatusBadgeComponent],
  templateUrl: './alert-feed.component.html',
})
export class AlertFeedComponent {
  @Input({ required: true }) alerts: Alert[] = [];
  @Output() resolve = new EventEmitter<string>();

  alertSeverityMeta = alertSeverityMeta;
  timeAgo = timeAgo;
}
