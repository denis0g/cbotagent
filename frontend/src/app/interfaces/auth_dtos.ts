export interface LoginRequest { usernameOrEmail: string; password: string; }
export interface RegisterRequest { userName: string; email: string; password: string; }
export interface LoginResponse { userName: string; accessToken: string; }
export interface RegisterResponse { userName: string; }
