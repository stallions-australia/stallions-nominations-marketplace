namespace Stallions.Server.Services;

public sealed class ServiceResult
{
    public bool Succeeded { get; }
    public string? Error { get; }
    public int HttpStatusCode { get; }

    private ServiceResult(bool succeeded, string? error, int status)
    { Succeeded = succeeded; Error = error; HttpStatusCode = status; }

    public static ServiceResult Ok() => new(true, null, 200);
    public static ServiceResult NotFound(string error = "Not found") => new(false, error, 404);
    public static ServiceResult Forbidden(string error = "Access denied") => new(false, error, 403);
    public static ServiceResult BadRequest(string error) => new(false, error, 400);
    public static ServiceResult Conflict(string error) => new(false, error, 409);
}

public sealed class ServiceResult<T>
{
    public bool Succeeded { get; }
    public T? Value { get; }
    public string? Error { get; }
    public int HttpStatusCode { get; }

    private ServiceResult(bool succeeded, T? value, string? error, int status)
    { Succeeded = succeeded; Value = value; Error = error; HttpStatusCode = status; }

    public static ServiceResult<T> Ok(T value) => new(true, value, null, 200);
    public static ServiceResult<T> Created(T value) => new(true, value, null, 201);
    public static ServiceResult<T> NotFound(string error = "Not found") => new(false, default, error, 404);
    public static ServiceResult<T> Forbidden(string error = "Access denied") => new(false, default, error, 403);
    public static ServiceResult<T> BadRequest(string error) => new(false, default, error, 400);
    public static ServiceResult<T> Conflict(string error) => new(false, default, error, 409);
}
