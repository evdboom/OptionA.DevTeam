using DevTeam.UnitTests;
var results = await TestRunner.RunAllAsync();
return results.Failed > 0 ? 1 : 0;
