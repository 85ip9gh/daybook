import { Component } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { Help } from './help/help';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, Help],
  template: '<router-outlet /><app-help />',
})
export class App {}
