namespace SQLockDemo.Services.DemoRunner;

public interface ISqlockTest
{
    Task RunAsync();
    string Name { get; }
    string Description { get; }
}