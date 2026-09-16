model BouncingBall
  parameter Real e = 0.7 "Coefficient of restitution";
  parameter Real g = 9.81 "Gravity acceleration";
  Real h(start = 1, fixed = true) "Height";
  Real v(start = 0, fixed = true) "Velocity";
  Boolean flying(start = true, fixed = true);
equation
  der(h) = v;
  der(v) = if flying then -g else 0;
  flying = not (h <= 0 and v <= 0);
  when h <= 0 then
    reinit(v, -e * pre(v));
  end when;
  annotation(
    Icon(
      coordinateSystem(preserveAspectRatio = true, extent = {{-100, -100}, {100, 100}}),
      graphics = {
        Ellipse(
          lineColor = {38, 86, 120},
          fillColor = {214, 228, 238},
          fillPattern = FillPattern.Solid,
          extent = {{-80, -80}, {80, 80}}),
        Text(
          extent = {{-60, -20}, {60, 20}},
          textString = "h")
      }));
end BouncingBall;
