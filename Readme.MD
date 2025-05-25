#### This project
Is a .NET 8.0 library that provides a simple and reliable way to coordinate distributed systems using existing database infrastructure.

#### Key Features

- Works with SQL Server
- Uses sp_getapplock to acquire locks
- Works at session level

Contains a simple API that is easy to integrate into existing applications.

#### How It Works

The library uses the sp_getapplock stored procedure to acquire locks. 
It uses a unique lock key to ensure that only one process has the lock at a time.
The lock key is composed of the name of the entity and its ID.

Acquiring the lock has a timeout of 30 seconds. If a lock is not acquired by then, an exception is thrown.

The lock is automatically released when the lock object is disposed.

#### Projects

- SQLock - SQL-based Distributed Lock
- SQLockDemo - SQL-based Distributed Lock Demo - a console application that demonstrates the usage of the SQLock library
- Data - Mimics a real world data project

#### Command Line Usage

`SQLockDemo --seed`
Seeds the database with some data -- 10 vehicles, if the database is empty.