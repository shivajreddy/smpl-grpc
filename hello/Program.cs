using Google.Protobuf;

//Create and populate
var p1 = new Person { Name = "smpl", Age = 30 };
Console.WriteLine($"p1:: Name: {p1.Name}, Age: {p1.Age}");

// serialize to bytes
byte[] bytes = p1.ToByteArray();

// Deserialize from bytes
var p2 = Person.Parser.ParseFrom(bytes);
Console.WriteLine($"p2:: Name: {p2.Name}, Age: {p2.Age}");

