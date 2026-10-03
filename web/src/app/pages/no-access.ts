import { Component } from '@angular/core';

@Component({
  selector: 'app-no-access',
  template: `
    <section class="page">
      <header class="page-head">
        <div>
          <h1>Nothing to show for your role</h1>
          <p>Your account can sign in, but none of the screens are open to the roles it holds.</p>
        </div>
      </header>
      <p class="note">Ask an administrator to give your account a role that fits your work.</p>
    </section>
  `,
})
export class NoAccess {}
