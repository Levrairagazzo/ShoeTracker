namespace ShoeTracker.Api.Dtos;

public record LoginRequest(string Email, string Password);

public record UserResponse(int Id, string Email);
