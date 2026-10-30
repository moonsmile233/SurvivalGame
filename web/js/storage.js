(function () {
  'use strict';

  // localStorage 唯一出入口（规范第 9 节）：业务代码禁止直接调用 localStorage。
  // 不可用（隐私模式 / 被禁用 / 配额满）时回退到内存，并暴露 available=false 供页面提示。
  var PREFIX = 'survival.';
  var memory = {};
  var available = true;

  try {
    var probeKey = PREFIX + '__probe__';
    window.localStorage.setItem(probeKey, '1');
    window.localStorage.removeItem(probeKey);
  } catch (error) {
    available = false;
    console.warn('[Storage] localStorage 不可用，回退到内存：' + error);
  }

  function readRaw(key) {
    if (available) {
      try {
        return window.localStorage.getItem(PREFIX + key);
      } catch (error) {
        available = false;
        console.warn('[Storage] 读取失败，回退到内存：' + error);
      }
    }
    return Object.prototype.hasOwnProperty.call(memory, key) ? memory[key] : null;
  }

  function writeRaw(key, value) {
    var text = String(value);
    memory[key] = text;

    if (!available) {
      return false;
    }

    try {
      window.localStorage.setItem(PREFIX + key, text);
      return true;
    } catch (error) {
      available = false;
      console.warn('[Storage] 写入失败，本次不再落盘：' + error);
      return false;
    }
  }

  function removeRaw(key) {
    delete memory[key];
    if (!available) {
      return;
    }
    try {
      window.localStorage.removeItem(PREFIX + key);
    } catch (error) {
      available = false;
      console.warn('[Storage] 删除失败：' + error);
    }
  }

  window.SurvivalStorage = {
    prefix: PREFIX,
    isAvailable: function () {
      return available;
    },
    get: function (key) {
      return readRaw(key);
    },
    getInt: function (key, fallback) {
      var raw = readRaw(key);
      if (raw === null || raw === '') {
        return fallback;
      }
      var value = parseInt(raw, 10);
      return isFinite(value) && value >= 0 ? value : fallback;
    },
    set: function (key, value) {
      return writeRaw(key, value);
    },
    remove: removeRaw
  };
})();
