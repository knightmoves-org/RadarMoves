namespace RadarMoves.Server.Models;

public readonly struct Result<T>(T? success, Exception? error) {
    public T? Success => success;
    public Exception? Error => error;
    public static Result<T> Ok(T success) => new(success, default);
    public static Result<T> Fail(Exception error) => new(default, error);

}
