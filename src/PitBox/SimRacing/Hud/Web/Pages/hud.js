(function () {
  "use strict";

  function themeLink() {
    var link = document.getElementById("theme");
    if (!link) {
      link = document.createElement("link");
      link.id = "theme";
      link.rel = "stylesheet";
      document.head.appendChild(link);
    }
    link.href = "/theme.css?t=" + Date.now();
  }

  themeLink();

  var kind = document.body.dataset.kind;
  if (!kind) return;

  var canvas = document.getElementById("frame");
  var ctx = canvas.getContext("2d");
  var socket = null;
  var retry = 1000;
  var pending = null;
  var drawing = false;

  function clear() {
    canvas.width = 0;
    canvas.height = 0;
  }

  function sendSize() {
    if (!socket || socket.readyState !== 1) return;
    var ratio = window.devicePixelRatio || 1;
    socket.send(JSON.stringify({ w: Math.round(window.innerWidth * ratio), h: Math.round(window.innerHeight * ratio) }));
  }

  function draw(blob) {
    if (blob.size === 0) { clear(); return; }
    pending = blob;
    if (drawing) return;
    drawing = true;
    var next = pending;
    pending = null;
    createImageBitmap(next).then(function (image) {
      var ratio = window.devicePixelRatio || 1;
      if (canvas.width !== image.width || canvas.height !== image.height) {
        canvas.width = image.width;
        canvas.height = image.height;
      }
      canvas.style.width = image.width / ratio + "px";
      canvas.style.height = image.height / ratio + "px";
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(image, 0, 0);
      image.close();
    }).catch(function () { }).then(function () {
      drawing = false;
      if (pending) draw(pending);
    });
  }

  function connect() {
    socket = new WebSocket("ws://" + location.host + "/ws/" + kind);
    socket.binaryType = "blob";
    socket.onopen = function () { retry = 1000; sendSize(); };
    socket.onmessage = function (e) {
      if (typeof e.data !== "string") { draw(e.data); return; }
      var message = JSON.parse(e.data);
      if (message.type === "theme") themeLink();
      else if (message.type === "look") canvas.style.opacity = message.opacity;
    };
    socket.onclose = function () {
      socket = null;
      clear();
      setTimeout(connect, retry);
      retry = Math.min(retry * 2, 5000);
    };
    socket.onerror = function () { };
  }

  var resizeTimer = 0;
  window.addEventListener("resize", function () {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(sendSize, 100);
  });
  connect();
})();
