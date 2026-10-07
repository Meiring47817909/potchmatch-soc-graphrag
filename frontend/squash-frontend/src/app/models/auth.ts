// Shapes matching IdentityService's AuthDtos.cs. ASP.NET Core serializes
// record property names as camelCase by default, which is why these are
// lowerCamelCase here even though the C# side uses PascalCase.

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  name: string;
  surname: string;
  email: string;
  password: string;
  clubId: string;
  isCoach: boolean;
}

export interface AuthResponse {
  token: string;
  playerId: string;
  name: string;
  surname: string;
  role: string;
  clubId: string;
}
