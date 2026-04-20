namespace DevTeam.TestInfrastructure;
public class AssertException(string test, string expected, string actual) : Exception($"Test: {test}, Expected: {expected}, Actual: {actual}")
{
}