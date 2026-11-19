import { Component, inject, signal } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../../services/authService';

@Component({
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './register.html',
  styleUrl: './register.css',
})
export class Register {
  private readonly fb = inject(FormBuilder);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly form = this.fb.nonNullable.group({
    userName: ['', [Validators.required, Validators.minLength(3)]],
    email: ['', [Validators.required, Validators.email]],
    password: ['', [Validators.required, Validators.minLength(6)]],
  });

  submit(): void {
    this.error.set('');
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.loading.set(true);
    this.auth.register(this.form.getRawValue()).subscribe({
      next: () => { this.loading.set(false); void this.router.navigate(['/login'], { queryParams: { registered: '1' } }); },
      error: (err: unknown) => { this.loading.set(false); this.error.set(this.message(err)); },
    });
  }

  private message(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 400 && typeof error.error === 'string' && /exist/i.test(error.error)) {
      return 'That username or email is already in use.';
    }
    if (error instanceof HttpErrorResponse && error.status >= 400 && error.status < 500) {
      return 'Please check your details and try again.';
    }
    return 'Error creating account. Please try again.';
  }
}
