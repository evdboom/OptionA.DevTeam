using DevTeam.ShellTests;

var results = await TestRunner.RunAllAsync();
return results.Failed > 0 ? 1 : 0;
