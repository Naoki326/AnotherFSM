!function(){"use strict";function e(e,t,n,o){return new(n||(n=Promise))((function(r,i){function l(e){try{a(o.next(e))}catch(e){i(e)}}function s(e){try{a(o.throw(e))}catch(e){i(e)}}function a(e){var t;e.done?r(e.value):(t=e.value,t instanceof n?t:new n((function(e){e(t)}))).then(l,s)}a((o=o.apply(e,t||[])).next())}))}const t="undefined"!=typeof window;let n=!1;try{if(t){const e=Object.defineProperty({},"passive",{get:()=>{n=!0}});window.addEventListener("testListener",e,e),window.removeEventListener("testListener",e,e)}}catch(e){console.warn(e)}const o=Object.freeze({enter:13,tab:9,delete:46,esc:27,space:32,up:38,down:40,left:37,right:39,end:35,home:36,del:46,backspace:8,insert:45,pageup:33,pagedown:34,shift:16});function r(e){if(!e)return null;let t=e.getAttributeNames().find((e=>e.startsWith("_bl_")));return t&&(t=t.substring(4)),t}function i(e){if(e instanceof Element){for(var t=[];e.nodeType===Node.ELEMENT_NODE;){var n=e.nodeName.toLowerCase();if(e.id){n="#"+e.id,t.unshift(n);break}for(var o=e,r=1;o=o.previousElementSibling;)o.nodeName.toLowerCase()==n&&r++;1!=r&&(n+=":nth-of-type("+r+")"),t.unshift(n),e=e.parentNode}return t.join(" > ")}}function l(e){let t;try{if(e)if("string"==typeof e)if("document"===e)t=document.documentElement;else if(e.indexOf("__.__")>0){let n=e.split("__.__"),o=0,r=document.querySelector(n[o++]);if(r)for(;n[o];)r=r[n[o]],o++;r instanceof HTMLElement&&(t=r)}else t=document.querySelector(e);else t=e;else t=document.body}catch(e){console.error(e)}return t}const s=!!(t&&"undefined"!=typeof document&&window.document&&window.document.createElement),a=["touchcancel","touchend","touchmove","touchenter","touchleave","touchstart"];function c(e){return{detail:e.detail,screenX:e.screenX,screenY:e.screenY,clientX:e.clientX,clientY:e.clientY,offsetX:e.offsetX,offsetY:e.offsetY,pageX:e.pageX,pageY:e.pageY,button:e.button,buttons:e.buttons,ctrlKey:e.ctrlKey,shiftKey:e.shiftKey,altKey:e.altKey,metaKey:e.metaKey,type:e.type}}function u(e){return{detail:e.detail,touches:d(e.touches),targetTouches:d(e.targetTouches),changedTouches:d(e.changedTouches),ctrlKey:e.ctrlKey,shiftKey:e.shiftKey,altKey:e.altKey,metaKey:e.metaKey,type:e.type}}function d(e){const t=[];for(let n=0;n<e.length;n++){const o=e[n];t.push({identifier:o.identifier,clientX:o.clientX,clientY:o.clientY,screenX:o.screenX,screenY:o.screenY,pageX:o.pageX,pageY:o.pageY})}return t}function f(e,t){Blazor&&Blazor.registerCustomEventType(e,{browserEventName:t,createEventArgs:e=>m("mouse",e)})}function p(e,t){Blazor&&Blazor.registerCustomEventType(e,{browserEventName:t,createEventArgs:e=>{const t=(n=e,Object.assign(Object.assign({},c(n)),{dataTransfer:n.dataTransfer?{dropEffect:n.dataTransfer.dropEffect,effectAllowed:n.dataTransfer.effectAllowed,files:Array.from(n.dataTransfer.files).map((e=>e.name)),items:Array.from(n.dataTransfer.items).map((e=>({kind:e.kind,type:e.type}))),types:n.dataTransfer.types}:null}));var n;const o=e.dataTransfer.getData("data-value"),r=e.dataTransfer.getData("offsetX"),i=e.dataTransfer.getData("offsetY");return t.dataTransfer.data={value:o,offsetX:Number(r),offsetY:Number(i)},t}})}function m(e,t){let n={target:{}};return"mouse"===e?n=Object.assign(Object.assign({},n),c(t)):"touch"===e&&(n=Object.assign(Object.assign({},n),u(t))),n.target=function(e){const t=e,n={},o=t.getAttributeNames().find((e=>e.startsWith("_bl_")));return o?(n.elementReferenceId=o,n.selector=`[${o}]`):n.selector=i(t),n.class=t.getAttribute("class"),n}(t.target),n}let h=0;const v={};var g=Object.freeze({__proto__:null,registerSliderEvents:function(t,o){v[h]=s;const r=document.querySelector("[data-app]"),i=!n||{passive:!0,capture:!0},l=!!n&&{passive:!0};return t.addEventListener("mousedown",s),t.addEventListener("touchstart",s),h++;function s(t){return e(this,void 0,void 0,(function*(){const e="touches"in t;c(t),r.addEventListener(e?"touchmove":"mousemove",c,l),function(e,t,n,o=!1){const r=i=>{n(i),e.removeEventListener(t,r,o)};e.addEventListener(t,r,o)}(r,e?"touchend":"mouseup",a,i),e?yield o.invokeMethodAsync("OnTouchStartInternal",m("touch",t)):yield o.invokeMethodAsync("OnMouseDownInternal",m("mouse",t))}))}function a(t){return e(this,void 0,void 0,(function*(){t.stopPropagation(),r.removeEventListener("touchmove",c,l),r.removeEventListener("mousemove",c,l),yield o.invokeMethodAsync("OnMouseUpInternal")}))}function c(t){return e(this,void 0,void 0,(function*(){const e="touches"in t,n={type:t.type,clientX:e?t.touches[0].clientX:t.clientX,clientY:e?t.touches[0].clientY:t.clientY};yield o.invokeMethodAsync("OnMouseMoveInternal",n)}))}},unregisterSliderEvents:function(e,t){if(e){const n=v[t];e.removeEventListener("mousedown",n),e.removeEventListener("touchstart",n),delete v[t]}}});let y=0;const w={};function b(e,t,n){e.style.height="0";const o=e.scrollHeight,r=parseInt(t,10)*parseFloat(n);e.style.height=Math.max(o,r)+"px"}var E=Object.freeze({__proto__:null,registerTextareaAutoGrowEvent:function(e){const t=e=>{const t=e.target;if(void 0===t.getAttribute("data-auto-grow"))return;const n=t.getAttribute("rows"),o=t.getAttribute("data-row-height");b(t,n,o)};return w[y]=t,e.addEventListener("input",t),y++},unregisterTextareaAutoGrowEvent:function(e,t){if(!e)return;const n=w[t];n&&e.removeEventListener("input",n)},calculateTextareaHeight:b}),T=function(e,t,n){var o=null,r=null,i=function(){o&&(clearTimeout(o),r=null,o=null)},l=function(){if(!t)return e.apply(this,arguments);var l=this,s=arguments,a=n&&!o;return i(),r=function(){e.apply(l,s)},o=setTimeout((function(){if(o=null,!a){var e=r;return r=null,e()}}),t),a?r():void 0};return l.cancel=i,l.flush=function(){var e=r;i(),e&&e()},l};var L=function(e,t,n){var o=null,r=null,i=n&&n.leading,l=n&&n.trailing;null==i&&(i=!0);null==l&&(l=!i);1==i&&(l=!1);var s=function(){o&&(clearTimeout(o),o=null)},a=function(){var n=i&&!o,s=this,a=arguments;if(r=function(){return e.apply(s,a)},o||(o=setTimeout((function(){if(o=null,l)return r()}),t)),n)return n=!1,r()};return a.cancel=s,a.flush=function(){var e=r;s(),e&&e()},a};function _(){var e,t;f("exmousedown","mousedown"),f("exmouseup","mouseup"),f("exclick","click"),f("exmouseleave","mouseleave"),f("exmouseenter","mouseenter"),f("exmousemove","mousemove"),e="extouchstart",t="touchstart",Blazor&&Blazor.registerCustomEventType(e,{browserEventName:t,createEventArgs:e=>m("touch",e)}),function(e,t){Blazor&&Blazor.registerCustomEventType(e,{browserEventName:t})}("transitionend","transitionend"),p("exdrop","drop"),Blazor&&Blazor.registerCustomEventType("auxclick",{browserEventName:"auxclick",createEventArgs:c})}const C=80;function x(e,t){e.style.transform=t,e.style.webkitTransform=t}function S(e){return"TouchEvent"===e.constructor.name}function M(e){return"KeyboardEvent"===e.constructor.name}const k={show(e,t,n={}){if(!t._ripple||!t._ripple.enabled)return;const o=document.createElement("span"),r=document.createElement("span");o.appendChild(r),o.className="m-ripple__container",n.class&&(o.className+=` ${n.class}`);const{radius:i,scale:l,x:s,y:a,centerX:c,centerY:u}=((e,t,n={})=>{let o=0,r=0;if(!M(e)){const n=t.getBoundingClientRect(),i=S(e)?e.touches[e.touches.length-1]:e;o=i.clientX-n.left,r=i.clientY-n.top}let i=0,l=.3;t._ripple&&t._ripple.circle?(l=.15,i=t.clientWidth/2,i=n.center?i:i+Math.sqrt((o-i)**2+(r-i)**2)/4):i=Math.sqrt(t.clientWidth**2+t.clientHeight**2)/2;const s=(t.clientWidth-2*i)/2+"px",a=(t.clientHeight-2*i)/2+"px";return{radius:i,scale:l,x:n.center?s:o-i+"px",y:n.center?a:r-i+"px",centerX:s,centerY:a}})(e,t,n),d=2*i+"px";r.className="m-ripple__animation",r.style.width=d,r.style.height=d,t.appendChild(o);const f=window.getComputedStyle(t);f&&"static"===f.position&&(t.style.position="relative",t.dataset.previousPosition="static"),r.classList.add("m-ripple__animation--enter"),r.classList.add("m-ripple__animation--visible"),x(r,`translate(${s}, ${a}) scale3d(${l},${l},${l})`),r.dataset.activated=String(performance.now()),setTimeout((()=>{r.classList.remove("m-ripple__animation--enter"),r.classList.add("m-ripple__animation--in"),x(r,`translate(${c}, ${u}) scale3d(1,1,1)`)}),0)},hide(e){if(!e||!e._ripple||!e._ripple.enabled)return;const t=e.getElementsByClassName("m-ripple__animation");if(0===t.length)return;const n=t[t.length-1];if(n.dataset.isHiding)return;n.dataset.isHiding="true";const o=performance.now()-Number(n.dataset.activated),r=Math.max(250-o,0);setTimeout((()=>{n.classList.remove("m-ripple__animation--in"),n.classList.add("m-ripple__animation--out"),setTimeout((()=>{var t;1===e.getElementsByClassName("m-ripple__animation").length&&e.dataset.previousPosition&&(e.style.position=e.dataset.previousPosition,delete e.dataset.previousPosition),(null===(t=n.parentNode)||void 0===t?void 0:t.parentNode)===e&&e.removeChild(n.parentNode)}),300)}),r)}};function A(e){const t={},n=e.currentTarget;if(n&&n._ripple&&!n._ripple.touched&&!e.rippleStop){if(e.rippleStop=!0,S(e))n._ripple.touched=!0,n._ripple.isTouch=!0;else if(n._ripple.isTouch)return;if(t.center=n._ripple.centered||M(e),n._ripple.class&&(t.class=n._ripple.class),S(e)){if(n._ripple.showTimerCommit)return;n._ripple.showTimerCommit=()=>{k.show(e,n,t)},n._ripple.showTimer=window.setTimeout((()=>{n&&n._ripple&&n._ripple.showTimerCommit&&(n._ripple.showTimerCommit(),n._ripple.showTimerCommit=null)}),C)}else k.show(e,n,t)}}function O(e){const t=e.currentTarget;if(t&&t._ripple)if(window.clearTimeout(t._ripple.showTimer),"touchend"===e.type&&t._ripple.showTimerCommit){t._ripple.showTimerCommit(),t._ripple.showTimerCommit=null;t._ripple.showTimer=setTimeout((()=>O(e)))}else window.setTimeout((()=>{t._ripple&&(t._ripple.touched=!1)})),k.hide(t)}function N(e){const t=e.currentTarget;t&&t._ripple&&(t._ripple.showTimerCommit&&(t._ripple.showTimerCommit=null),window.clearTimeout(t._ripple.showTimer))}function H(e){const t=e.currentTarget;t.keyboardRipple||e.keyCode!==o.enter&&e.keyCode!==o.space||(t.keyboardRipple=!0,A(e))}function B(e){e.currentTarget.keyboardRipple=!1,O(e)}function I(e){const t=e.currentTarget;!0===t.keyboardRipple&&(t.keyboardRipple=!1,O(e))}function Y(e,t,n){let o=!1;t?o=!0:k.hide(e);const r=t||{};e._ripple=e._ripple||{},e._ripple.enabled=o,e._ripple=Object.assign(Object.assign({},e._ripple),{centered:r.center,class:r.class,circle:r.circle}),o&&!n?(e.addEventListener("touchstart",A,{passive:!0}),e.addEventListener("touchend",O,{passive:!0}),e.addEventListener("touchmove",N,{passive:!0}),e.addEventListener("touchcancel",O),e.addEventListener("mousedown",A),e.addEventListener("mouseup",O),e.addEventListener("mouseleave",O),e.addEventListener("keydown",H),e.addEventListener("keyup",B),e.addEventListener("blur",I),e.addEventListener("dragstart",O,{passive:!0})):!o&&n&&P(e)}function P(e){e.removeEventListener("mousedown",A),e.removeEventListener("touchstart",A),e.removeEventListener("touchend",O),e.removeEventListener("touchmove",N),e.removeEventListener("touchcancel",O),e.removeEventListener("mouseup",O),e.removeEventListener("mouseleave",O),e.removeEventListener("keydown",H),e.removeEventListener("keyup",B),e.removeEventListener("dragstart",O),e.removeEventListener("blur",I),e._ripple.enabled=!1}function W(e){if(!e||e.nodeType!==Node.ELEMENT_NODE)return 0;const t=+window.getComputedStyle(e).getPropertyValue("z-index");return t||W(e.parentNode)}function D(e){var t={};t.offsetTop=e.offsetTop||0,t.offsetLeft=e.offsetLeft||0,t.scrollHeight=e.scrollHeight||0,t.scrollWidth=e.scrollWidth||0,t.scrollLeft=e.scrollLeft||0,t.scrollTop=e.scrollTop||0,t.clientTop=e.clientTop||0,t.clientLeft=e.clientLeft||0,t.clientHeight=e.clientHeight||0,t.clientWidth=e.clientWidth||0;var n=function(e){var t=new Object;if(t.x=0,t.y=0,null!==e&&e.getBoundingClientRect){var n=document.documentElement,o=e.getBoundingClientRect(),r=n.scrollLeft,i=n.scrollTop;t.offsetWidth=o.width,t.offsetHeight=o.height,t.relativeTop=o.top,t.relativeBottom=o.bottom,t.relativeLeft=o.left,t.relativeRight=o.right,t.absoluteLeft=o.left+r,t.absoluteTop=o.top+i}return t}(e);return t.offsetWidth=Math.round(n.offsetWidth)||0,t.offsetHeight=Math.round(n.offsetHeight)||0,t.relativeTop=Math.round(n.relativeTop)||0,t.relativeBottom=Math.round(n.relativeBottom)||0,t.relativeLeft=Math.round(n.relativeLeft)||0,t.relativeRight=Math.round(n.relativeRight)||0,t.absoluteLeft=Math.round(n.absoluteLeft)||0,t.absoluteTop=Math.round(n.absoluteTop)||0,t}window.onload=function(){var e;_(),e="pastewithdata",Blazor&&Blazor.registerCustomEventType(e,{browserEventName:"paste",createEventArgs:e=>({type:e.type,pastedData:e.clipboardData.getData("text")})}),function(){const e=new MutationObserver(((e,n)=>{for(const n of e){if("childList"===n.type&&n.addedNodes.length>0)for(const e of n.addedNodes)e instanceof HTMLElement&&e.nodeType===Node.ELEMENT_NODE&&e.hasAttribute("ripple")&&!e._ripple&&Y(e,t(e),!1);if("attributes"===n.type){const e=n.target;e.hasAttribute("ripple")&&!e._ripple&&("ripple"===n.attributeName?Y(e,t(e),!1):!e.hasAttribute("ripple")&&e._ripple&&(P(e),delete e._ripple))}if("attributes"===n.type&&"ripple"===n.attributeName){const e=n.target;e._ripple&&Y(e,t(e),e._ripple.enabled)}if("childList"===n.type&&n.removedNodes.length>0)for(const e of n.removedNodes)e instanceof HTMLElement&&e.nodeType===Node.ELEMENT_NODE&&e._ripple&&(P(e),delete e._ripple)}}));function t(e){const t=e.getAttribute("ripple");if("string"!=typeof t&&!t||"false"===t)return null;const n={};return t.split("&").forEach((e=>{"center"===e?n.center=!0:"circle"===e?n.circle=!0:n.class=e.trim()})),n}const n=document.querySelectorAll("[ripple]");for(const e of n)Y(e,t(e),!1);e.observe(document,{childList:!0,subtree:!0,attributes:!0,attributeFilter:["ripple"],attributeOldValue:!1})}()};var X={};function q(){return document.activeElement.getAttribute("id")||""}function z(e=[],t=[]){const n={};return e&&(e.forEach((e=>n[e]=window[e])),n.pageYOffset=K()),t&&t.forEach((e=>n[e]=document.documentElement[e])),n}function $(e){return"HTML"!==e.tagName&&"BODY"!==e.tagName&&1==e.nodeType}function R(e=[],t){const n=[W(l(t))],o=[...document.getElementsByClassName("m-menu__content--active"),...document.getElementsByClassName("m-dialog__content--active")];for(let t=0;t<o.length;t++)e.includes(o[t])||n.push(W(o[t]));return Math.max(...n)}function F(e,t,n,o,r,i){if(!r){var l=document.querySelector(i);o.nodeType&&l.appendChild(o)}var s={activator:{},content:{},relativeYOffset:0,offsetParentLeft:0};if(e){var a=document.querySelector(t);s.activator=j(a,n),s.activator.offsetLeft=a.offsetLeft,s.activator.offsetTop=n?0:a.offsetTop}return function(e,t){if(!t||!t.style||"none"!==t.style.display)return void e();t.style.display="inline-block",e(),t.style.display="none"}((()=>{if(o){if(o.offsetParent){const t=V(o.offsetParent);s.relativeYOffset=K()+t.top,e?(s.activator.top-=s.relativeYOffset,s.activator.left-=window.pageXOffset+t.left):s.offsetParentLeft=t.left}s.content=j(o,n),s.content.offsetLeft=o.offsetLeft,s.content.offsetTop=o.offsetTop}}),o),s}function K(){let e=window.pageYOffset;const t=parseInt(document.documentElement.style.getPropertyValue("--m-body-scroll-y"));return t&&(e+=Math.abs(t)),e}function j(e,t){if(!e)return{};const n=V(e);if(!t){const t=window.getComputedStyle(e);n.left=parseInt(t.marginLeft),n.top=parseInt(t.marginTop)}return n}function V(e){if(!e||!e.nodeType)return null;const t=e.getBoundingClientRect();return{top:Math.round(t.top),left:Math.round(t.left),bottom:Math.round(t.bottom),right:Math.round(t.right),width:Math.round(t.width),height:Math.round(t.height)}}function U(e,t,n,o){e.preventDefault();const r=e.key;if("ArrowLeft"===r||"Backspace"===r){if("Backspace"===r){const e={type:r,index:t,value:""};o&&o.invokeMethodAsync("Invoke",e)}G(t-1,n)}else"ArrowRight"===r&&G(t+1,n)}function G(e,t){if(e<0)G(0,t);else if(e>=t.length)G(t.length-1,t);else if(document.activeElement!==t[e]){l(t[e]).focus()}}function Z(e,t,n){const o=l(n[t]);o&&document.activeElement===o&&o.select()}function J(e,t,n,o){const r=e.target.value;if(r&&""!==r&&(G(t+1,n),o)){const e={type:"Input",index:t,value:r};o.invokeMethodAsync("Invoke",e)}}function Q(){var e,t,n="weird_get_top_level_domain=cookie",o=document.location.hostname.split(".");for(e=o.length-1;e>=0;e--)if(t=o.slice(e).join("."),document.cookie=n+";domain=."+t+";",document.cookie.indexOf(n)>-1)return document.cookie=n.split("=")[0]+"=;domain=."+t+";expires=Thu, 01 Jan 1970 00:00:01 GMT;",t}function ee(e){e.stopPropagation()}var te=Object.freeze({__proto__:null,getZIndex:W,getDomInfo:function(e,t="body"){var n={},o=l(e);if(o)if(o.style&&"none"===o.style.display){var r=o.cloneNode(!0);r.style.display="inline-block",r.style["z-index"]=-1e3,o.parentElement.appendChild(r),n=D(r),o.parentElement.removeChild(r)}else n=D(o);return n},getParentClientWidthOrWindowInnerWidth:function(e){return e.parentElement?e.parentElement.clientWidth:window.innerWidth},triggerEvent:function(e,t,n,o){var r=l(e),i=document.createEvent(t);return i.initEvent(n),o&&i.stopPropagation(),r.dispatchEvent(i)},setProperty:function(e,t,n){l(e)[t]=n},getBoundingClientRect:function(e,t="body"){var n,o;let r=l(e);var i={};if(r&&r.getBoundingClientRect)if(r.style&&"none"===r.style.display){var s=r.cloneNode(!0);s.style.display="inline-block",s.style["z-index"]=-1e3,null===(n=document.querySelector(t))||void 0===n||n.appendChild(s),i=s.getBoundingClientRect(),null===(o=document.querySelector(t))||void 0===o||o.removeChild(s)}else i=r.getBoundingClientRect();return i},addHtmlElementEventListener:function(e,t,n,o,r){let l;if(l="window"==e?window:"document"==e?document.documentElement:document.querySelector(e),!l)return!1;var s=(null==r?void 0:r.key)||`${e}:${t}`;const d={};var f=e=>{var t;if((null==r?void 0:r.stopPropagation)&&e.stopPropagation(),("boolean"!=typeof e.cancelable||e.cancelable)&&(null==r?void 0:r.preventDefault)&&e.preventDefault(),(null==r?void 0:r.relatedTarget)&&(null===(t=document.querySelector(r.relatedTarget))||void 0===t?void 0:t.contains(e.relatedTarget)))return;let o={};if(a.includes(e.type))o=u(e);else if(e instanceof MouseEvent)o=c(e);else for(var l in e)"string"!=typeof e[l]&&"number"!=typeof e[l]||(o[l]=e[l]);if(e.target&&e.target!==window&&e.target!==document){o.target={};const t=e.target,n=t.getAttributeNames().find((e=>e.startsWith("_bl_")));n?(o.target.elementReferenceId=n,o.target.selector=`[${n}]`):o.target.selector=i(t),o.target.class=t.getAttribute("class")}n.invokeMethodAsync("Invoke",o)};return(null==r?void 0:r.debounce)&&r.debounce>0?d.listener=T(f,r.debounce):(null==r?void 0:r.throttle)&&r.throttle>0?d.listener=L(f,r.throttle,{trailing:!0}):d.listener=f,d.options=o,d.handle=n,X[s]?X[s].push(d):X[s]=[d],l.addEventListener(t,d.listener,d.options),!0},removeHtmlElementEventListener:function(e,t,n){let o;o="window"==e?window:"document"==e?document.documentElement:document.querySelector(e);var r=X[n=n||`${e}:${t}`];r&&(r.forEach((e=>{e.handle.dispose(),null==o||o.removeEventListener(t,e.listener,e.options)})),X[n]=[])},addMouseleaveEventListener:function(e){var t=document.querySelector(e);t&&t.addEventListener()},contains:function(e,t){const n=l(e);return!(!n||!n.contains)&&n.contains(l(t))},equalsOrContains:function(e,t){const n=l(e),o=l(t);return!!n&&n.contains&&!!o&&(n==o||n.contains(o))},copy:function(e){navigator.clipboard?navigator.clipboard.writeText(e).then((function(){console.log("Async: Copying to clipboard was successful!")}),(function(e){console.error("Async: Could not copy text: ",e)})):function(e){var t=document.createElement("textarea");t.value=e,t.style.top="0",t.style.left="0",t.style.position="fixed",document.body.appendChild(t),t.focus(),t.select();try{var n=document.execCommand("copy")?"successful":"unsuccessful";console.log("Fallback: Copying text command was "+n)}catch(e){console.error("Fallback: Oops, unable to copy",e)}document.body.removeChild(t)}(e)},focus:function(e,t=!1){let n=l(e);n instanceof HTMLElement?n.focus({preventScroll:t}):console.error("Unable to focus an invalid element")},select:function(e){let t=l(e);if(!(t instanceof HTMLInputElement||t instanceof HTMLTextAreaElement))throw new Error("Unable to select an invalid element");t.select()},hasFocus:function(e){let t=l(e);return document.activeElement===t},blur:function(e){l(e).blur()},log:function(e){console.log(e)},scrollIntoView:function(e,t){let n=l(e);n instanceof HTMLElement&&(null===t||null==t?n.scrollIntoView():"boolean"==typeof t?n.scrollIntoView(t):n.scrollIntoView({block:null==t.block?void 0:t.block,inline:null==t.inline?void 0:t.inline,behavior:t.behavior}))},scrollIntoParentView:function(e,t=!1,n=!1,o=1,r="smooth"){const i=l(e);if(i instanceof HTMLElement){let e=i;for(;o>0;)if(e=e.parentElement,o--,!e)return;const l={behavior:r};if(t)if(n)l.left=i.offsetLeft;else{const t=i.offsetLeft-e.offsetLeft;t-e.scrollLeft<0?l.left=t:t+i.offsetWidth-e.scrollLeft>e.offsetWidth&&(l.left=t+i.offsetWidth-e.offsetWidth)}else if(n)l.top=i.offsetTop;else{const t=i.offsetTop-e.offsetTop;t-e.scrollTop<0?l.top=t:t+i.offsetHeight-e.scrollTop>e.offsetHeight&&(l.top=t+i.offsetHeight-e.offsetHeight)}(l.left||l.top)&&e.scrollTo(l)}},scrollTo:function(e,t){let n=l(e);if(n instanceof HTMLElement){const e={left:null===t.left?void 0:t.left,top:null===t.top?void 0:t.top,behavior:t.behavior};n.scrollTo(e)}},scrollToTarget:function(e,t=null,n=0){const o=document.querySelector(e);if(o){let e;e=t?o.offsetTop:o.getBoundingClientRect().top+window.scrollY;(t?document.querySelector(t):document.documentElement).scrollTo({top:e-n,behavior:"smooth"})}},scrollToElement:function(e,t,n){const o=l(e);if(!o)return;const r=o.getBoundingClientRect().top+window.pageYOffset-t;window.scrollTo({top:r,behavior:n})},scrollToActiveElement:function(e,t=".active",n="center",o=!1){let r,i=l(e);if("string"==typeof t&&(r=e.querySelector(t)),!i||!r)return;const s="center"===n?r.offsetTop-i.offsetHeight/2+r.offsetHeight/2:r.offsetTop-n;i.scrollTo({top:s,behavior:o?"smooth":"auto"})},addClsToFirstChild:function(e,t){var n=l(e);n.firstElementChild&&n.firstElementChild.classList.add(t)},removeClsFromFirstChild:function(e,t){var n=l(e);n.firstElementChild&&n.firstElementChild.classList.remove(t)},getAbsoluteTop:function e(t){var n=t.offsetTop;return null!=t.offsetParent&&(n+=e(t.offsetParent)),n},getAbsoluteLeft:function e(t){var n=t.offsetLeft;return null!=t.offsetParent&&(n+=e(t.offsetParent)),n},addElementToBody:function(e){document.body.appendChild(e)},delElementFromBody:function(e){document.body.removeChild(e)},addElementTo:function(e,t){let n=l(t);n&&e&&n.appendChild(e)},delElementFrom:function(e,t){let n=l(t);n&&e&&n.removeChild(e)},getActiveElement:q,focusDialog:function e(t,n=0){let o=document.querySelector(t);o&&!o.hasAttribute("disabled")&&setTimeout((()=>{o.focus(),"#"+q()!==t&&n<10&&e(t,n+1)}),10)},getWindow:function(){return{innerWidth:window.innerWidth,innerHeight:window.innerHeight,pageXOffset:window.pageXOffset,pageYOffset:window.pageYOffset,isTop:0==window.scrollY,isBottom:window.scrollY+window.innerHeight==document.body.clientHeight}},getWindowAndDocumentProps:z,css:function(e,t,n=null){var o=l(e);if("string"==typeof t)o.style[t]=n;else for(let e in t)t.hasOwnProperty(e)&&(o.style[e]=t[e])},addCls:function(e,t){let n=l(e);"string"==typeof t?n.classList.add(t):n.classList.add(...t)},removeCls:function(e,t){let n=l(e);"string"==typeof t?n.classList.remove(t):n.classList.remove(...t)},elementScrollIntoView:function(e){let t=l(e);t&&t.scrollIntoView({behavior:"smooth",block:"nearest",inline:"start"})},getScroll:function(){return{x:window.pageXOffset,y:window.pageYOffset}},getScrollParent:function(e,t=undefined){null!=t||(t=s?window:void 0);let n=e;for(;n&&n!==t&&$(n);){const{overflowY:e}=window.getComputedStyle(n);if(/scroll|auto|overlay/i.test(e))return n;n=n.parentNode}return t},getScrollTop:function(e){const t="scrollTop"in e?e.scrollTop:e.pageYOffset;return Math.max(t,0)},getInnerText:function(e){return l(e).innerText},getMenuOrDialogMaxZIndex:R,getMaxZIndex:function(){return[...document.all].reduce(((e,t)=>Math.max(e,+window.getComputedStyle(t).zIndex||0)),0)},getStyle:function(e,t){return(e=l(e)).currentStyle?e.currentStyle[t]:window.getComputedStyle?document.defaultView.getComputedStyle(e,null).getPropertyValue(t):void 0},getTextAreaInfo:function(e){var t={},n=l(e);return t.scrollHeight=n.scrollHeight||0,e.currentStyle?(t.lineHeight=parseFloat(e.currentStyle["line-height"]),t.paddingTop=parseFloat(e.currentStyle["padding-top"]),t.paddingBottom=parseFloat(e.currentStyle["padding-bottom"]),t.borderBottom=parseFloat(e.currentStyle["border-bottom"]),t.borderTop=parseFloat(e.currentStyle["border-top"])):window.getComputedStyle&&(t.lineHeight=parseFloat(document.defaultView.getComputedStyle(e,null).getPropertyValue("line-height")),t.paddingTop=parseFloat(document.defaultView.getComputedStyle(e,null).getPropertyValue("padding-top")),t.paddingBottom=parseFloat(document.defaultView.getComputedStyle(e,null).getPropertyValue("padding-bottom")),t.borderBottom=parseFloat(document.defaultView.getComputedStyle(e,null).getPropertyValue("border-bottom")),t.borderTop=parseFloat(document.defaultView.getComputedStyle(e,null).getPropertyValue("border-top"))),Object.is(NaN,t.borderTop)&&(t.borderTop=1),Object.is(NaN,t.borderBottom)&&(t.borderBottom=1),t},disposeObj:function(e){},upsertThemeStyle:function(e,t){const n=document.getElementById(e);n&&document.head.removeChild(n);const o=document.createElement("style");o.id=e,o.type="text/css",o.innerHTML=t,document.head.insertAdjacentElement("beforeend",o)},getImageDimensions:function(e){return new Promise((function(t,n){var o=new Image;o.src=e,o.onload=function(){t({width:o.width,height:o.height,hasError:!1})},o.onerror=function(){t({width:0,height:0,hasError:!0})}}))},enablePreventDefaultForEvent:function(e,t,n){const o=l(e);o&&("keydown"===t?o.addEventListener(t,(e=>{Array.isArray(n)?n.includes(e.code)&&e.preventDefault():e.preventDefault()})):o.addEventListener(t,(e=>{e.preventDefault&&e.preventDefault()})))},getBoundingClientRects:function(e){for(var t=document.querySelectorAll(e),n=[],o=0;o<t.length;o++){var r=t[o],i={id:r.id,rect:r.getBoundingClientRect()};n.push(i)}return n},getSize:function(e,t){var n=l(e),o=n.style.display,r=n.style.overflow;n.style.display="",n.style.overflow="hidden";var i=n["offset"+t.charAt(0).toUpperCase()+t.slice(1)]||0;return n.style.display=o,n.style.overflow=r,i},getProp:function(e,t){if("window"===e)return window[t];var n=l(e);return n?n[t]:null},updateWindowTransition:function(e,t,n){var o=l(e),r=o.querySelector(".m-window__container");if(n){var i=l(n);r.style.height=i.clientHeight+"px"}else t?(r.classList.add("m-window__container--is-active"),r.style.height=o.clientHeight+"px"):(r.style.height="",r.classList.remove("m-window__container--is-active"))},getScrollHeightWithoutHeight:function(e){var t=l(e);if(!t)return 0;var n=t.style.height;t.style.height="0";var o=t.scrollHeight;return t.style.height=n,o},registerTextFieldOnMouseDown:function(e,t,n){if(!e||!t)return;const o=e=>{if(e.target!==l(t)&&(e.preventDefault(),e.stopPropagation()),n){const t={Detail:e.detail,ScreenX:e.screenX,ScreenY:e.screenY,ClientX:e.clientX,ClientY:e.clientY,OffsetX:e.offsetX,OffsetY:e.offsetY,PageX:e.pageX,PageY:e.pageY,Button:e.button,Buttons:e.buttons,CtrlKey:e.ctrlKey,ShiftKey:e.shiftKey,AltKey:e.altKey,MetaKey:e.metaKey,Type:e.type};n.invokeMethodAsync("Invoke",t)}};e.addEventListener("mousedown",o);const i={listener:o,handle:n},s=`registerTextFieldOnMouseDown_${r(e)}`;X[s]=[i]},unregisterTextFieldOnMouseDown:function(e){const t=`registerTextFieldOnMouseDown_${r(e)}`,n=X[t];n&&n.length&&n.forEach((t=>{t.handle.dispose(),e&&e.removeEventListener("mousedown",t.listener)}))},containsActiveElement:function(e){var t=l(e);return t&&t.contains?t.contains(document.activeElement):null},copyChild:function(e){"string"==typeof e&&(e=document.querySelector(e)),e&&(e.setAttribute("contenteditable","true"),e.focus(),document.execCommand("selectAll",!1,null),document.execCommand("copy"),document.execCommand("unselect"),e.blur(),e.removeAttribute("contenteditable"))},copyText:function(e){if(navigator.clipboard)navigator.clipboard.writeText(e).then((function(){console.log("Async: Copying to clipboard was successful!")}),(function(e){console.error("Async: Could not copy text: ",e)}));else{var t=document.createElement("textarea");t.value=e,t.readOnly=!0,t.style.top="0",t.style.left="0",t.style.position="fixed",document.body.appendChild(t),t.focus(),t.select();try{var n=document.execCommand("copy")?"successful":"unsuccessful";console.log("Fallback: Copying text command was "+n)}catch(e){console.error("Fallback: Oops, unable to copy",e)}document.body.removeChild(t)}},getMenuableDimensions:F,invokeMultipleMethod:function(e,t,n,o,r,i,l,s,a){var c={windowAndDocument:null,dimensions:null,zIndex:0};return c.windowAndDocument=z(e,t),c.dimensions=F(n,o,r,i,l,s),c.zIndex=R([i],a),c},registerOTPInputOnInputEvent:function(e,t){for(let n=0;n<e.length;n++){const o=o=>J(o,n,e,t),r=t=>Z(t,n,e),i=o=>U(o,n,e,t);e[n].addEventListener("input",o),e[n].addEventListener("focus",r),e[n].addEventListener("keyup",i),e[n]._optInput={inputListener:o,focusListener:r,keyupListener:i}}},unregisterOTPInputOnInputEvent:function(e){for(let t=0;t<e.length;t++){const n=e[t];n&&n._optInput&&(n.removeEventListener("input",n._optInput.inputListener),n.removeEventListener("focus",n._optInput.focusListener),n.removeEventListener("keyup",n._optInput.keyupListener))}},getListIndexWhereAttributeExists:function(e,t,n){const o=document.querySelectorAll(e);if(!o)return-1;let r=-1;for(let e=0;e<o.length;e++)if(o[e].getAttribute(t)===n){r=e;break}return r},scrollToTile:function(e,t,n,o){var r=document.querySelectorAll(t);if(!r)return;let i=r[n];if(!i)return;const l=document.querySelector(e);if(!l)return;const s=l.scrollTop,a=l.clientHeight;s>i.offsetTop-8?l.scrollTo({top:i.offsetTop-i.clientHeight,behavior:"smooth"}):s+a<i.offsetTop+i.clientHeight+8&&l.scrollTo({top:i.offsetTop-a+2*i.clientHeight,behavior:"smooth"})},getElementTranslateY:function(e){const t=window.getComputedStyle(e),n=t.transform||t.webkitTransform,o=n.slice(7,n.length-1).split(", ")[5];return Number(o)},checkIfThresholdIsExceededWhenScrolling:function(e,t,n){if(!e||!t)return;let o;if(o="window"==t?window:"document"==t?document.documentElement:document.querySelector(t),!o)return void console.warn("[MInfiniteScroll] failed to get parent element with selector:",t);const r=e.getBoundingClientRect().top;return(o===window?window.innerHeight:o.getBoundingClientRect().bottom)>=r-n},get_top_domain:Q,setCookie:function(e,t){if(null!=t){var n=Q();n?isNaN(n[0])&&(n=`.${n}`):n="";var o=new Date;o.setTime(o.getTime()+2592e6),document.cookie=`${e}=${escape(null==t?void 0:t.toString())};path=/;expires=${o.toUTCString()};domain=${n}`}},getCookie:function(e){const t=new RegExp(`(^| )${e}=([^;]*)(;|$)`),n=document.cookie.match(t);return n?unescape(n[2]):null},registerDragEvent:function(e,t){if(e){const n=r(e),o=e=>{if(t){const n=e.target.getAttribute(t);e.dataTransfer.setData(t,n),e.dataTransfer.setData("offsetX",e.offsetX.toString()),e.dataTransfer.setData("offsetY",e.offsetY.toString())}};X[`${n}:dragstart`]=[{listener:o}],e.addEventListener("dragstart",o)}},unregisterDragEvent:function(e){const t=r(e);if(t){const n=`${t}:dragstart`;X[n]&&X[n].forEach((t=>{e.removeEventListener("dragstart",t.listener)}))}},resizableDataTable:function(e){const t=e.querySelector("table"),n=t.querySelector(".m-data-table-header").getElementsByTagName("tr")[0],o=n?n.children:[];if(!o)return;t.style.overflow="hidden";const r=t.offsetHeight;for(var i=0;i<o.length;i++){const e=o[i],t=e.querySelector(".m-data-table-header__col-resize");if(!t)continue;t.style.height=r+"px";let n=e.firstElementChild.offsetWidth;n=n+32+18+1+1,e.style.minWidth||(e.minWidth=n,e.style.minWidth=n+"px"),l(t)}function l(n){let r,i,l,a,c,u;n.addEventListener("click",(e=>e.stopPropagation())),n.addEventListener("mousedown",(function(e){i=e.target.parentElement,l=i.nextElementSibling,r=e.pageX,u=t.offsetWidth;var n=function(e){if("border-box"==s(e,"box-sizing"))return 0;var t=s(e,"padding-left"),n=s(e,"padding-right");return parseInt(t)+parseInt(n)}(i);a=i.offsetWidth-n,l&&(c=l.offsetWidth-n)})),document.addEventListener("mousemove",(function(n){if(i){let o=n.pageX-r;e.classList.contains("m-data-table--rtl")&&(o=0-o);let s=a+o;i.style.width=s+"px";if(e.classList.contains("m-data-table--resizable-overflow"))return void(t.style.width=u+o+"px");if(e.classList.contains("m-data-table--resizable-independent")){let e=c-o;const t=a+c;o>0?l&&e<l.minWidth&&(e=l.minWidth,s=t-e):s<i.minWidth&&(s=i.minWidth,e=t-s),i.style.width=s+"px",l&&(l.style.width=e+"px")}}})),document.addEventListener("mouseup",(function(e){if(i)for(let e=0;e<o.length;e++){const t=o[e];t.style.width=t.offsetWidth+"px"}i=void 0,l=void 0,r=void 0,c=void 0,a=void 0,u=void 0}))}function s(e,t){return window.getComputedStyle(e,null).getPropertyValue(t)}},updateDataTableResizeHeight:function(e){const t=e.querySelector("table"),n=t.querySelector(".m-data-table-header").getElementsByTagName("tr")[0],o=n?n.children:[];if(!o)return;const r=t.offsetHeight;for(var i=0;i<o.length;i++){o[i].querySelector(".m-data-table-header__col-resize").style.height=r+"px"}},addStopPropagationEvent:function(e,t){l(e).addEventListener(t,ee)},removeStopPropagationEvent:function(e,t){l(e).removeEventListener(t,ee)},historyBack:function(){history.back()},historyGo:function(e){history.go(e)},historyReplace:function(e){history.replaceState(null,"",e)},registerTableScrollEvent:function(e){const t=()=>{const t=e.scrollWidth,n=e.clientWidth,o=e.scrollLeft,r=e.parentElement.classList.contains("m-data-table--rtl");Math.abs(t-((r?-o:o)+n))<1?(e.classList.remove("scrolling"),e.classList.remove("scrolled-to-left"),e.classList.add("scrolled-to-right")):Math.abs(o-(r?t-n:0))<1?(e.classList.remove("scrolling"),e.classList.remove("scrolled-to-right"),e.classList.add("scrolled-to-left")):(e.classList.remove("scrolled-to-right"),e.classList.remove("scrolled-to-left"),e.classList.add("scrolling"))};t(),e.addEventListener("scroll",t),e._m_table_scroll_event=t},unregisterTableScrollEvent:function(e){const t=e._m_table_scroll_event;t&&(e.removeEventListener("scroll",t),delete e._m_table_scroll_event)},updateTabSlider:function(e,t,n,o,r){if(!e)return void console.warn("[MTab] the element of slider wrapper is not found");if(!t)return void console.warn("[MTab] the element of tab to be activated is not found");const i=o?t.scrollHeight:n,l=o?0:t.offsetLeft,s=o?0:t.offsetLeft+t.offsetWidth,a=t.offsetTop,c=o?n:t.clientWidth;e.style.width=`${c}px`,e.style.height=`${i}px`,r||(e.style.left=`${l}px`),r&&(e.style.right=`${s}px`),o&&(e.style.top=`${a}px`)},isScrollNearBottom:function(e,t=200){return!!e&&e.scrollHeight-(e.scrollTop+e.clientHeight)<t},matchesSelector:function(e,n){if(!(t&&"undefined"!=typeof CSS&&void 0!==CSS.supports&&CSS.supports(`selector(${n})`)))return!1;try{return!!e&&e.matches(n)}catch(e){return!1}}});window.MasaBlazor={interop:Object.assign(Object.assign(Object.assign({},te),g),E),xgplayerPlugins:[]}}();
//# sourceMappingURL=masa-blazor.js.map