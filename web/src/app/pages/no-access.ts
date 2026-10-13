import { Component } from '@angular/core';

@Component({
  selector: 'app-no-access',
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Nothing to show for your role</h1>
          <p>Your role has no screens.</p>
        </div>
      </header>
      <p class="note">Ask an administrator for a role.</p>
    </section>
  `,
})
export class NoAccess {}
