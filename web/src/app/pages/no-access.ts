import { Component } from '@angular/core';

@Component({
  selector: 'app-no-access',
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Nothing to show for your role</h1>
          <p>Your account can sign in, but none of the screens built so far are open to it.</p>
        </div>
      </header>
      <p class="note">
        Administrators manage users through the API. A screen for that has not been built yet.
      </p>
    </section>
  `,
})
export class NoAccess {}
