model RCTest
  Modelica.Electrical.Analog.Sources.StepVoltage source(V = 1, startTime = 0.1);
  Modelica.Electrical.Analog.Basic.Resistor resistor(R = 1000);
  Modelica.Electrical.Analog.Basic.Capacitor capacitor(C = 0.001, v(start = 0, fixed = true));
  Modelica.Electrical.Analog.Basic.Ground ground;
equation
  connect(source.p, resistor.p);
  connect(resistor.n, capacitor.p);
  connect(capacitor.n, source.n);
  connect(source.n, ground.p);
  annotation(experiment(StartTime = 0, StopTime = 5, Tolerance = 1e-6, Interval = 0.01));
end RCTest;
