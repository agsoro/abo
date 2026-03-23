import { BpmnModdle } from 'bpmn-moddle';
import { assign, map, pick, isFunction } from 'min-dash';

function isConnection(element) {
  return !!element.sourceRef;
}

function isBoundaryEvent(element) {
  return !!element.attachedToRef;
}

function findElementInTree(currentElement, targetElement, visited = new Set()) {

  if (currentElement === targetElement) return true;

  if (visited.has(currentElement)) return false;

  visited.add(currentElement);

  // If currentElement has no outgoing connections, return false
  if (!currentElement.outgoing || currentElement.outgoing.length === 0) return false;

  // Recursively check each outgoing element
  for (let nextElement of currentElement.outgoing.map(out => out.targetRef)) {
    if (findElementInTree(nextElement, targetElement, visited)) {
      return true;
    }
  }

  return false;
}

const DEFAULT_TASK_HEIGHT = 80;
const DEFAULT_TASK_WIDTH = 100;

function getDefaultSize(element) {
  if (is(element, 'bpmn:SubProcess')) {
    return { width: DEFAULT_TASK_WIDTH, height: DEFAULT_TASK_HEIGHT };
  }

  if (is(element, 'bpmn:Task')) {
    return { width: DEFAULT_TASK_WIDTH, height: DEFAULT_TASK_HEIGHT };
  }

  if (is(element, 'bpmn:Gateway')) {
    return { width: 50, height: 50 };
  }

  if (is(element, 'bpmn:Event')) {
    return { width: 36, height: 36 };
  }

  if (is(element, 'bpmn:Participant')) {
    return { width: 400, height: 100 };
  }

  if (is(element, 'bpmn:Lane')) {
    return { width: 400, height: 100 };
  }

  if (is(element, 'bpmn:DataObjectReference')) {
    return { width: 36, height: 50 };
  }

  if (is(element, 'bpmn:DataStoreReference')) {
    return { width: 50, height: 50 };
  }

  if (is(element, 'bpmn:TextAnnotation')) {
    return { width: DEFAULT_TASK_WIDTH, height: 30 };
  }

  return { width: DEFAULT_TASK_WIDTH, height: DEFAULT_TASK_HEIGHT };
}

function is(element, type) {
  return element.$instanceOf(type);
}

const DEFAULT_CELL_WIDTH = 150;
const DEFAULT_CELL_HEIGHT = 140;

function getMid(bounds) {
  return {
    x: bounds.x + bounds.width / 2,
    y: bounds.y + bounds.height / 2
  };
}

function getDockingPoint(point, rectangle, dockingDirection = 'r', targetOrientation = 'top-left') {

  // ensure we end up with a specific docking direction
  // based on the targetOrientation, if <h|v> is being passed
  if (dockingDirection === 'h') {
    dockingDirection = /left/.test(targetOrientation) ? 'l' : 'r';
  }

  if (dockingDirection === 'v') {
    dockingDirection = /top/.test(targetOrientation) ? 't' : 'b';
  }

  if (dockingDirection === 't') {
    return { original: point, x: point.x, y: rectangle.y };
  }

  if (dockingDirection === 'r') {
    return { original: point, x: rectangle.x + rectangle.width, y: point.y };
  }

  if (dockingDirection === 'b') {
    return { original: point, x: point.x, y: rectangle.y + rectangle.height };
  }

  if (dockingDirection === 'l') {
    return { original: point, x: rectangle.x, y: point.y };
  }

  throw new Error('unexpected dockingDirection: <' + dockingDirection + '>');
}

/**
 * Modified Manhattan layout: Uses space between grid columns to route connections
 * if direct connection is not possible.
 * @param {*} source
 * @param {*} target
 * @param layoutGrid
 * @returns waypoints
 */
function connectElements(source, target, layoutGrid) {
  const sourceDi = source.di;
  const targetDi = target.di;

  const sourceBounds = sourceDi.get('bounds');
  const targetBounds = targetDi.get('bounds');

  const sourceMid = getMid(sourceBounds);
  const targetMid = getMid(targetBounds);

  const dX = target.gridPosition.col - source.gridPosition.col;
  const dY = target.gridPosition.row - source.gridPosition.row;

  const dockingSource = `${(dY > 0 ? 'bottom' : 'top')}-${dX > 0 ? 'right' : 'left'}`;
  const dockingTarget = `${(dY > 0 ? 'top' : 'bottom')}-${dX > 0 ? 'left' : 'right'}`;

  const baseSourceGrid = source.grid || source.attachedToRef?.grid;
  const baseTargetGrid = target.grid;

  // Source === Target ==> Build loop
  if (dX === 0 && dY === 0) {
    const { x, y } = coordinatesToPosition(source.gridPosition.row, source.gridPosition.col);
    return [
      getDockingPoint(sourceMid, sourceBounds, 'r', dockingSource),
      { x: baseSourceGrid ? x + (baseSourceGrid.getGridDimensions()[1] + 1) * DEFAULT_CELL_WIDTH : x + DEFAULT_CELL_WIDTH, y: sourceMid.y },
      { x: baseSourceGrid ? x + (baseSourceGrid.getGridDimensions()[1] + 1) * DEFAULT_CELL_WIDTH : x + DEFAULT_CELL_WIDTH, y: y },
      { x: targetMid.x, y: y },
      getDockingPoint(targetMid, targetBounds, 't', dockingTarget)
    ];
  }

  // negative dX indicates connection from future to past
  if (dX < 0) {

    const { y } = coordinatesToPosition(source.gridPosition.row, source.gridPosition.col);

    const offsetY = DEFAULT_CELL_HEIGHT / 2;

    if (sourceMid.y >= targetMid.y) {

      // edge goes below
      const maxExpanded = getMaxExpandedBetween(source, target, layoutGrid);

      if (maxExpanded) {
        return [
          getDockingPoint(sourceMid, sourceBounds, 'b'),
          { x: sourceMid.x, y: !baseSourceGrid ? y + DEFAULT_CELL_HEIGHT + maxExpanded * DEFAULT_CELL_HEIGHT : y + (baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
          { x: targetMid.x, y: !baseSourceGrid ? y + DEFAULT_CELL_HEIGHT + maxExpanded * DEFAULT_CELL_HEIGHT : y + (baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
          getDockingPoint(targetMid, targetBounds, 'b')
        ];
      }

      return [
        getDockingPoint(sourceMid, sourceBounds, 'b'),
        { x: sourceMid.x, y: !baseSourceGrid ? y + DEFAULT_CELL_HEIGHT : y + (baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
        { x: targetMid.x, y: !baseSourceGrid ? y + DEFAULT_CELL_HEIGHT : y + (baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
        getDockingPoint(targetMid, targetBounds, 'b')
      ];
    } else {

      // edge goes above
      const bendY = sourceMid.y - offsetY;

      return [
        getDockingPoint(sourceMid, sourceBounds, 't'),
        { x: sourceMid.x, y: bendY },
        { x: targetMid.x, y: bendY },
        getDockingPoint(targetMid, targetBounds, 't')
      ];
    }
  }

  // connect horizontally
  if (dY === 0) {
    if (isDirectPathBlocked(source, target, layoutGrid)) {
      const { y } = coordinatesToPosition(source.gridPosition.row, source.gridPosition.col);

      // Route on bottom
      return [
        getDockingPoint(sourceMid, sourceBounds, 'b'),
        { x: sourceMid.x, y: !baseSourceGrid ? y + DEFAULT_CELL_HEIGHT : y + (baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
        { x: targetMid.x, y: !baseSourceGrid ? y + DEFAULT_CELL_HEIGHT : y + (baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
        getDockingPoint(targetMid, targetBounds, 'b')
      ];
    } else {

      // if space is clear, connect directly
      const firstPoint = getDockingPoint(sourceMid, sourceBounds, 'h', dockingSource);
      const lastPoint = getDockingPoint(targetMid, targetBounds, 'h', dockingTarget);
      if (baseSourceGrid) {
        firstPoint.y = sourceBounds.y + DEFAULT_TASK_HEIGHT / 2 ;
      }

      if (baseTargetGrid) {
        lastPoint.y = targetBounds.y + DEFAULT_TASK_HEIGHT / 2 ;
      }
      return [
        firstPoint,
        lastPoint
      ];
    }
  }

  // connect vertically
  if (dX === 0) {
    if (isDirectPathBlocked(source, target, layoutGrid)) {

      // Route parallel
      const yOffset = -Math.sign(dY) * DEFAULT_CELL_HEIGHT / 2;
      return [
        getDockingPoint(sourceMid, sourceBounds, 'r'),
        { x: sourceMid.x + DEFAULT_CELL_WIDTH / 2, y: sourceMid.y }, // out right
        { x: targetMid.x + DEFAULT_CELL_WIDTH / 2, y: targetMid.y + yOffset },
        { x: targetMid.x, y: targetMid.y + yOffset },
        getDockingPoint(targetMid, targetBounds, Math.sign(yOffset) > 0 ? 'b' : 't')
      ];
    } else {

      // if space is clear, connect directly
      return [ getDockingPoint(sourceMid, sourceBounds, 'v', dockingSource),
        getDockingPoint(targetMid, targetBounds, 'v', dockingTarget)
      ];
    }
  }

  const directManhattan = directManhattanConnect(source, target, layoutGrid);

  if (directManhattan) {
    const startPoint = getDockingPoint(sourceMid, sourceBounds, directManhattan[0], dockingSource);
    const endPoint = getDockingPoint(targetMid, targetBounds, directManhattan[1], dockingTarget);

    const midPoint = directManhattan[0] === 'h' ? { x: endPoint.x, y: startPoint.y } : { x: startPoint.x, y: endPoint.y };

    return [
      startPoint,
      midPoint,
      endPoint
    ];
  }
  const yOffset = -Math.sign(dY) * DEFAULT_CELL_HEIGHT / 2;

  return [
    getDockingPoint(sourceMid, sourceBounds, 'r', dockingSource),
    { x: sourceMid.x + DEFAULT_CELL_WIDTH / 2, y: sourceMid.y }, // out right
    { x: sourceMid.x + DEFAULT_CELL_WIDTH / 2, y: targetMid.y + yOffset }, // to target row
    { x: targetMid.x - DEFAULT_CELL_WIDTH / 2, y: targetMid.y + yOffset }, // to target column
    { x: targetMid.x - DEFAULT_CELL_WIDTH / 2, y: targetMid.y }, // to mid
    getDockingPoint(targetMid, targetBounds, 'l', dockingTarget)
  ];
}

// helpers /////
function coordinatesToPosition(row, col) {
  return {
    width: DEFAULT_CELL_WIDTH,
    height: DEFAULT_CELL_HEIGHT,
    x: col * DEFAULT_CELL_WIDTH,
    y: row * DEFAULT_CELL_HEIGHT
  };
}

function getBounds(element, row, col, shift, attachedTo) {
  let { width, height } = getDefaultSize(element);
  const { x, y } = shift;

  // Center in cell
  if (!attachedTo) {
    return {
      width: element.isExpanded ? element.grid.getGridDimensions()[1] * DEFAULT_CELL_WIDTH + width : width,
      height: element.isExpanded ? element.grid.getGridDimensions()[0] * DEFAULT_CELL_HEIGHT + height : height,
      x: (col * DEFAULT_CELL_WIDTH) + (DEFAULT_CELL_WIDTH - width) / 2 + x,
      y: row * DEFAULT_CELL_HEIGHT + (DEFAULT_CELL_HEIGHT - height) / 2 + y
    };
  }

  const hostBounds = attachedTo.di.bounds;

  return {
    width, height,
    x: Math.round(hostBounds.x + hostBounds.width / 2 - width / 2),
    y: Math.round(hostBounds.y + hostBounds.height - height / 2)
  };
}

function isDirectPathBlocked(source, target, layoutGrid) {
  const { row: sourceRow, col: sourceCol } = source.gridPosition;
  const { row: targetRow, col: targetCol } = target.gridPosition;

  const dX = targetCol - sourceCol;
  const dY = targetRow - sourceRow;

  let totalElements = 0;

  if (dX) {
    totalElements += layoutGrid.getElementsInRange({ row: sourceRow, col: sourceCol }, { row: sourceRow, col: targetCol }).length;
  }

  if (dY) {
    totalElements += layoutGrid.getElementsInRange({ row: sourceRow, col: targetCol }, { row: targetRow, col: targetCol }).length;
  }

  return totalElements > 2;
}

function directManhattanConnect(source, target, layoutGrid) {
  const { row: sourceRow, col: sourceCol } = source.gridPosition;
  const { row: targetRow, col: targetCol } = target.gridPosition;

  const dX = targetCol - sourceCol;
  const dY = targetRow - sourceRow;

  // Only directly connect left-to-right flow
  if (!(dX > 0 && dY !== 0)) {
    return;
  }

  // If below, go down then horizontal
  if (dY > 0) {
    let totalElements = 0;
    const bendPoint = { row: targetRow, col: sourceCol };
    totalElements += layoutGrid.getElementsInRange({ row: sourceRow, col: sourceCol }, bendPoint).length;
    totalElements += layoutGrid.getElementsInRange(bendPoint, { row: targetRow, col: targetCol }).length;

    return totalElements > 2 ? false : [ 'v', 'h' ];
  } else {

    // If above, go horizontal than vertical
    let totalElements = 0;
    const bendPoint = { row: sourceRow, col: targetCol };

    totalElements += layoutGrid.getElementsInRange({ row: sourceRow, col: sourceCol }, bendPoint).length;
    totalElements += layoutGrid.getElementsInRange(bendPoint, { row: targetRow, col: targetCol }).length;

    return totalElements > 2 ? false : [ 'h', 'v' ];
  }
}


function getMaxExpandedBetween(source, target, layoutGrid) {

  const hostSource = source.attachedToRef ? source.attachedToRef : source;
  const hostTarget = target.attachedToRef ? target.attachedToRef : target;


  const [ sourceRow, sourceCol ] = layoutGrid.find(hostSource);
  const [ , targetCol ] = layoutGrid.find(hostTarget);

  const firstCol = sourceCol < targetCol ? sourceCol : targetCol;
  const lastCol = sourceCol < targetCol ? targetCol : sourceCol;

  const elementsInRange = layoutGrid.getAllElements().filter(element => element.gridPosition.row === sourceRow && element.gridPosition.col > firstCol && element.gridPosition.col < lastCol);

  return elementsInRange.reduce((acc, cur) => {
    if (cur.grid?.getGridDimensions()[0] > acc) return cur.grid?.getGridDimensions()[0];
  }, 0);
}

class Grid {
  constructor() {
    this.grid = [];
  }

  add(element, position) {
    if (!position) {
      this._addStart(element);
      return;
    }

    const [ row, col ] = position;
    if (!row && !col) {
      this._addStart(element);
    }

    if (!this.grid[row]) {
      this.grid[row] = [];
    }

    if (this.grid[row][col]) {
      throw new Error('Grid is occupied please ensure the place you insert at is not occupied');
    }

    this.grid[row][col] = element;
  }

  createRow(afterIndex) {
    if (!afterIndex && !Number.isInteger(afterIndex)) {
      this.grid.push([]);
    } else {
      this.grid.splice(afterIndex + 1, 0, []);
    }
  }

  _addStart(element) {
    this.grid.push([ element ]);
  }

  addAfter(element, newElement) {
    if (!element) {
      this._addStart(newElement);
    }
    const [ row, col ] = this.find(element);
    this.grid[row].splice(col + 1, 0, newElement);
  }

  addBelow(element, newElement) {
    if (!element) {
      this._addStart(newElement);
    }

    const [ row, col ] = this.find(element);

    // We are at the bottom of the current grid - add empty row below
    if (!this.grid[row + 1]) {
      this.grid[row + 1] = [];
    }

    // The element below is already occupied - insert new row
    if (this.grid[row + 1][col]) {
      this.grid.splice(row + 1, 0, []);
    }

    if (this.grid[row + 1][col]) {
      throw new Error('Grid is occupied and we could not find a place - this should not happen');
    }

    this.grid[row + 1][col] = newElement;
  }

  find(element) {
    let row, col;
    row = this.grid.findIndex((row) => {
      col = row.findIndex((el) => {
        return el === element;
      });

      return col !== -1;
    });

    return [ row, col ];
  }

  get(row, col) {
    return (this.grid[row] || [])[col];
  }

  getElementsInRange({ row: startRow, col: startCol }, { row: endRow, col: endCol }) {
    const elements = [];

    if (startRow > endRow) {
      [ startRow, endRow ] = [ endRow, startRow ];
    }

    if (startCol > endCol) {
      [ startCol, endCol ] = [ endCol, startCol ];
    }

    for (let row = startRow; row <= endRow; row++) {
      for (let col = startCol; col <= endCol; col++) {
        const element = this.get(row, col);

        if (element) {
          elements.push(element);
        }
      }
    }

    return elements;
  }

  adjustGridPosition(element) {
    let [ row, col ] = this.find(element);
    const [ , maxCol ] = this.getGridDimensions();

    if (col < maxCol - 1) {

      // add element in next column
      this.grid[row].length = maxCol;
      this.grid[row][maxCol] = element;
      this.grid[row][col] = null;

    }
  }

  adjustRowForMultipleIncoming(elements, currentElement) {
    const results = elements.map(element => this.find(element));

    // filter only rows that currently exist, excluding any future or non-existent rows
    const lowestRow = Math.min(...results
      .map(result => result[0])
      .filter(row => row >= 0));

    const [ row , col ] = this.find(currentElement);

    // if element doesn't already exist in current row, add element
    if (lowestRow < row && !this.grid[lowestRow][col]) {
      this.grid[lowestRow][col] = currentElement;
      this.grid[row][col] = null;
    }
  }

  adjustColumnForMultipleIncoming(elements, currentElement) {
    const results = elements.map(element => this.find(element));

    // filter only col that currently exist, excluding any future or non-existent col
    const maxCol = Math.max(...results
      .map(result => result[1])
      .filter(col => col >= 0));

    const [ row , col ] = this.find(currentElement);

    // add to the next column
    if (maxCol + 1 > col) {
      this.grid[row][maxCol + 1] = currentElement;
      this.grid[row][col] = null;
    }
  }

  getAllElements() {
    const elements = [];

    for (let row = 0; row < this.grid.length; row++) {
      for (let col = 0; col < this.grid[row].length; col++) {
        const element = this.get(row, col);

        if (element) {
          elements.push(element);
        }
      }
    }

    return elements;
  }

  getGridDimensions() {
    const numRows = this.grid.length;
    let maxCols = 0;

    for (let i = 0; i < numRows; i++) {
      const currentRowLength = this.grid[i].length;
      if (currentRowLength > maxCols) {
        maxCols = currentRowLength;
      }
    }

    return [ numRows , maxCols ];
  }

  elementsByPosition() {
    const elements = [];

    this.grid.forEach((row, rowIndex) => {
      row.forEach((element, colIndex) => {
        if (!element) {
          return;
        }
        elements.push({
          element,
          row: rowIndex,
          col: colIndex
        });
      });
    });

    return elements;
  }

  getElementsTotal() {
    const flattenedGrid = this.grid.flat();
    const uniqueElements = new Set(flattenedGrid.filter(value => value));
    return uniqueElements.size;
  }

  /**
   *
   * @param {number} afterIndex - number is integer
   * @param {number=} colCount - number is positive integer
   */
  createCol(afterIndex, colCount) {
    this.grid.forEach((row, rowIndex) => {
      this.expandRow(rowIndex, afterIndex, colCount);
    });
  }

  /**
   * @param {number} rowIndex - is positive integer
   * @param {number} afterIndex - is integer
   * @param {number=} colCount - is positive integer
   */
  expandRow(rowIndex, afterIndex, colCount) {
    if (!Number.isInteger(rowIndex) || rowIndex < 0 || rowIndex > this.rowCount - 1) return;

    const placeholder = Number.isInteger(colCount) && colCount > 0 ? Array(colCount) : Array(1);

    const row = this.grid[rowIndex];

    if (!afterIndex && !Number.isInteger(afterIndex)) {
      row.splice(row.length, 0, ...placeholder);
    } else {
      row.splice(afterIndex + 1, 0, ...placeholder);
    }
  }
}

class DiFactory {
  constructor(moddle) {
    this.moddle = moddle;
  }

  create(type, attrs) {
    return this.moddle.create(type, attrs || {});
  }

  createDiBounds(bounds) {
    return this.create('dc:Bounds', bounds);
  }

  createDiLabel() {
    return this.create('bpmndi:BPMNLabel', {
      bounds: this.createDiBounds()
    });
  }

  createDiShape(semantic, bounds, attrs) {
    return this.create('bpmndi:BPMNShape', assign({
      bpmnElement: semantic,
      bounds: this.createDiBounds(bounds)
    }, attrs));
  }

  createDiWaypoints(waypoints) {
    var self = this;

    return map(waypoints, function(pos) {
      return self.createDiWaypoint(pos);
    });
  }

  createDiWaypoint(point) {
    return this.create('dc:Point', pick(point, [ 'x', 'y' ]));
  }

  createDiEdge(semantic, waypoints, attrs) {
    return this.create('bpmndi:BPMNEdge', assign({
      bpmnElement: semantic,
      waypoint: this.createDiWaypoints(waypoints)
    }, attrs));
  }

  createDiPlane(attrs) {
    return this.create('bpmndi:BPMNPlane', attrs);
  }

  createDiDiagram(attrs) {
    return this.create('bpmndi:BPMNDiagram', attrs);
  }
}

var attacherHandler = {
  'addToGrid': ({ element, grid, visited }) => {
    const nextElements = [];

    const attachedOutgoing = (element.attachers || [])
      .map(attacher => (attacher.outgoing || []).reverse())
      .flat()
      .map(out => out.targetRef);

    // handle boundary events
    attachedOutgoing.forEach((nextElement, index, arr) => {
      if (visited.has(nextElement)) {
        return;
      }

      // Add below and to the right of the element
      insertIntoGrid(nextElement, element, grid);
      nextElements.push(nextElement);
      visited.add(nextElement);
    });

    return nextElements;
  },

  'createElementDi': ({ element, row, col, diFactory, shift }) => {
    const hostBounds = getBounds(element, row, col, shift);

    const DIs = [];
    (element.attachers || []).forEach((att, i, arr) => {
      att.gridPosition = { row, col };
      const bounds = getBounds(att, row, col, shift, element);

      // distribute along lower edge
      bounds.x = hostBounds.x + (i + 1) * (hostBounds.width / (arr.length + 1)) - bounds.width / 2;

      const attacherDi = diFactory.createDiShape(att, bounds, {
        id: att.id + '_di'
      });
      att.di = attacherDi;
      att.gridPosition = { row, col };

      DIs.push(attacherDi);
    });

    return DIs;
  },

  'createConnectionDi': ({ element, row, col, layoutGrid, diFactory, shift }) => {
    const attachers = element.attachers || [];

    return attachers.flatMap(att => {
      const outgoing = att.outgoing || [];

      return outgoing.map(out => {
        const target = out.targetRef;
        const waypoints = connectElements(att, target, layoutGrid);

        // Correct waypoints if they don't automatically attach to the bottom
        ensureExitBottom(att, waypoints, [ row, col ]);

        return diFactory.createDiEdge(out, waypoints, {
          id: out.id + '_di'
        });
      });
    });
  }
};


function insertIntoGrid(newElement, host, grid) {
  const [ row, col ] = grid.find(host);

  // Grid is occupied
  if (grid.get(row + 1, col) || grid.get(row + 1, col + 1)) {
    grid.createRow(row);
  }

  grid.add(newElement, [ row + 1, col + 1 ]);
}

function ensureExitBottom(source, waypoints, [ row, col ]) {

  const sourceDi = source.di;
  const sourceBounds = sourceDi.get('bounds');
  const sourceMid = getMid(sourceBounds);

  const dockingPoint = getDockingPoint(sourceMid, sourceBounds, 'b');
  if (waypoints[0].x === dockingPoint.x && waypoints[0].y === dockingPoint.y) {
    return;
  }

  const baseSourceGrid = source.grid || source.attachedToRef?.grid;

  if (waypoints.length === 2) {
    const newStart = [
      dockingPoint,
      { x: dockingPoint.x, y: !baseSourceGrid ? (row + 1) * DEFAULT_CELL_HEIGHT : (row + baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
      { x: !baseSourceGrid ? (col + 1) * DEFAULT_CELL_WIDTH : (col + baseSourceGrid.getGridDimensions()[1] + 1) * DEFAULT_CELL_WIDTH, y: !baseSourceGrid ? (row + 1) * DEFAULT_CELL_HEIGHT : (row + baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
      { x: !baseSourceGrid ? (col + 1) * DEFAULT_CELL_WIDTH : (col + baseSourceGrid.getGridDimensions()[1] + 1) * DEFAULT_CELL_WIDTH, y: !baseSourceGrid ? (row + 0.5) * DEFAULT_CELL_HEIGHT : row * DEFAULT_CELL_HEIGHT + DEFAULT_CELL_HEIGHT / 2 },
    ];

    waypoints.splice(0, 1, ...newStart);
    return;
  }

  // add waypoints to exit bottom and connect to existing path
  const newStart = [
    dockingPoint,
    { x: dockingPoint.x, y: !baseSourceGrid ? (row + 1) * DEFAULT_CELL_HEIGHT : (row + baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
    { x: waypoints[1].x, y: !baseSourceGrid ? (row + 1) * DEFAULT_CELL_HEIGHT : (row + baseSourceGrid.getGridDimensions()[0] + 1) * DEFAULT_CELL_HEIGHT },
  ];

  waypoints.splice(0, 1, ...newStart);
}

var elementHandler = {
  'createElementDi': ({ element, row, col, diFactory, shift }) => {

    const bounds = getBounds(element, row, col, shift);

    const options = {
      id: element.id + '_di'
    };

    if (is(element, 'bpmn:ExclusiveGateway')) {
      options.isMarkerVisible = true;
    }

    if (element.isExpanded) {
      options.isExpanded = true;
    }

    const shapeDi = diFactory.createDiShape(element, bounds, options);
    element.di = shapeDi;
    element.gridPosition = { row, col };

    return shapeDi;
  }
};

var outgoingHandler = {
  'addToGrid': ({ element, grid, visited, stack }) => {
    let nextElements = [];

    // Handle outgoing paths
    const outgoing = (element.outgoing || [])
      .map(out => out.targetRef)
      .filter(el => el);

    let previousElement = null;

    if (outgoing.length > 1 && isNextElementTasks(outgoing)) {
      grid.adjustGridPosition(element);
    }

    outgoing.forEach((nextElement, index, arr) => {
      if (visited.has(nextElement)) {
        return;
      }

      // Prevents revisiting future incoming elements and ensures proper traversal without early exit.
      if ((previousElement || stack.length > 0) && isFutureIncoming(nextElement, visited) && !checkForLoop(nextElement, visited)) {
        return;
      }

      if (!previousElement) {
        grid.addAfter(element, nextElement);
      }

      else if (is(element, 'bpmn:ExclusiveGateway') && is(nextElement, 'bpmn:ExclusiveGateway')) {
        grid.addAfter(previousElement, nextElement);
      }
      else {
        grid.addBelow(arr[index - 1], nextElement);
      }

      // Is self-looping
      if (nextElement !== element) {
        previousElement = nextElement;
      }

      nextElements.unshift(nextElement);
      visited.add(nextElement);
    });

    // Sort elements by priority to ensure proper stack placement
    nextElements = sortByType(nextElements, 'bpmn:ExclusiveGateway'); // TODO: sort by priority
    return nextElements;
  },

  'createConnectionDi': ({ element, row, col, layoutGrid, diFactory }) => {
    const outgoing = element.outgoing || [];

    return outgoing.map(out => {
      const target = out.targetRef;
      const waypoints = connectElements(element, target, layoutGrid);

      const connectionDi = diFactory.createDiEdge(out, waypoints, {
        id: out.id + '_di'
      });

      return connectionDi;
    });

  }
};


// helpers /////

function sortByType(arr, type) {
  const nonMatching = arr.filter(item => !is(item,type));
  const matching = arr.filter(item => is(item,type));

  return [ ...matching, ...nonMatching ];

}

function checkForLoop(element, visited) {
  for (const incomingElement of element.incoming) {
    if (!visited.has(incomingElement.sourceRef)) {
      return findElementInTree(element, incomingElement.sourceRef);
    }
  }
}


function isFutureIncoming(element, visited) {
  if (element.incoming.length > 1) {
    for (const incomingElement of element.incoming) {
      if (!visited.has(incomingElement.sourceRef)) {
        return true;
      }
    }
  }
  return false;
}

function isNextElementTasks(elements) {
  return elements.every(element => is(element, 'bpmn:Task'));
}

var incomingHandler = {
  'addToGrid': ({ element, grid, visited }) => {
    const nextElements = [];

    const incoming = (element.incoming || [])
      .map(out => out.sourceRef)
      .filter(el => el);

    // adjust the row if it is empty
    if (incoming.length > 1) {
      grid.adjustColumnForMultipleIncoming(incoming, element);
      grid.adjustRowForMultipleIncoming(incoming, element);
    }
    return nextElements;
  },
};

const handlers = [ elementHandler, incomingHandler, outgoingHandler, attacherHandler ];

class Layouter {
  constructor() {
    this.moddle = new BpmnModdle();
    this.diFactory = new DiFactory(this.moddle);
    this._handlers = handlers;
  }

  handle(operation, options) {
    return this._handlers
      .filter(handler => isFunction(handler[operation]))
      .map(handler => handler[operation](options));

  }

  async layoutProcess(xml) {
    const moddleObj = await this.moddle.fromXML(xml);
    const { rootElement } = moddleObj;

    this.diagram = rootElement;

    const firstRootProcess = this.getProcess();

    if (firstRootProcess) {

      this.setExpandedPropertyToModdleElements(moddleObj);

      this.setExecutedProcesses(firstRootProcess);

      this.createGridsForProcesses();

      this.cleanDi();

      this.createRootDi(firstRootProcess);

      this.drawProcesses();
    }

    return (await this.moddle.toXML(this.diagram, { format: true })).xml;
  }

  createGridsForProcesses() {
    const processes = this.layoutedProcesses.sort((a, b) => b.level - a.level);

    // create and add grids for each process
    // root processes should be processed last for element expanding
    for (const process of processes) {

      // add base grid with collapsed elements
      process.grid = this.createGridLayout(process);

      expandGridHorizontally(process.grid);
      expandGridVertically(process.grid);

      if (process.isExpanded) {
        const [ rowCount, colCount ] = process.grid.getGridDimensions();
        if (rowCount === 0) process.grid.createRow();
        if (colCount === 0) process.grid.createCol();
      }

    }
  }

  setExpandedPropertyToModdleElements(bpmnModel) {
    const allElements = bpmnModel.elementsById;
    if (allElements) {
      for (const element of Object.values(allElements)) {
        if (element.$type === 'bpmndi:BPMNShape' && element.isExpanded === true) element.bpmnElement.isExpanded = true;
      }
    }
  }

  setExecutedProcesses(firstRootProcess) {
    this.layoutedProcesses = [];

    const executionStack = [ firstRootProcess ];

    while (executionStack.length > 0) {
      const executedProcess = executionStack.pop();
      this.layoutedProcesses.push(executedProcess);
      executedProcess.level = executedProcess.$parent === this.diagram ? 0 : executedProcess.$parent.level + 1;

      const nextProcesses = executedProcess.get('flowElements').filter(flowElement => is(flowElement, 'bpmn:SubProcess'));

      executionStack.splice(executionStack.length, 0, ...nextProcesses);
    }
  }

  cleanDi() {
    this.diagram.diagrams = [];
  }

  createGridLayout(root) {
    const grid = new Grid();

    const flowElements = root.flowElements || [];
    const elements = flowElements.filter(el => !is(el,'bpmn:SequenceFlow'));

    // check for empty process/subprocess
    if (!flowElements) {
      return grid;
    }

    bindBoundaryEventsWithHosts (flowElements);

    // Depth-first-search
    const visited = new Set();
    while (visited.size < elements.filter(element => !element.attachedToRef).length) {
      const startingElements = flowElements.filter(el => {
        return !isConnection(el) &&
            !isBoundaryEvent(el) &&
            (!el.incoming || !hasOtherIncoming(el)) &&
            !visited.has(el);
      });

      const stack = [ ...startingElements ];

      startingElements.forEach(el => {
        grid.add(el);
        visited.add(el);
      });

      this.handleGrid(grid,visited,stack);

      if (grid.getElementsTotal() !== elements.length) {
        const gridElements = grid.getAllElements();
        const missingElements = elements.filter(el => !gridElements.includes(el) && !isBoundaryEvent(el));
        if (missingElements.length > 0) {
          stack.push(missingElements[0]);
          grid.add(missingElements[0]);
          visited.add(missingElements[0]);
          this.handleGrid(grid,visited,stack);
        }
      }
    }
    return grid;
  }

  generateDi(layoutGrid , shift, procDi) {
    const diFactory = this.diFactory;

    const prePlaneElement = procDi ? procDi : this.diagram.diagrams[0];

    const planeElement = prePlaneElement.plane.get('planeElement');

    // Step 1: Create DI for all elements
    layoutGrid.elementsByPosition().forEach(({ element, row, col }) => {
      const dis = this
        .handle('createElementDi', { element, row, col, layoutGrid, diFactory, shift })
        .flat();

      planeElement.push(...dis);
    });

    // Step 2: Create DI for all connections
    layoutGrid.elementsByPosition().forEach(({ element, row, col }) => {
      const dis = this
        .handle('createConnectionDi', { element, row, col, layoutGrid, diFactory, shift })
        .flat();

      planeElement.push(...dis);
    });
  }

  handleGrid(grid, visited, stack) {
    while (stack.length > 0) {
      const currentElement = stack.pop();

      const nextElements = this.handle('addToGrid', { element: currentElement, grid, visited, stack });

      nextElements.flat().forEach(el => {
        stack.push(el);
        visited.add(el);
      });
    }
  }

  getProcess() {
    return this.diagram.get('rootElements').find(el => el.$type === 'bpmn:Process');
  }

  createRootDi(processes) {
    this.createProcessDi(processes);
  }

  createProcessDi(element) {
    const diFactory = this.diFactory;

    const planeDi = diFactory.createDiPlane({
      id: 'BPMNPlane_' + element.id,
      bpmnElement: element
    });
    const diagramDi = diFactory.createDiDiagram({
      id: 'BPMNDiagram_' + element.id,
      plane: planeDi
    });

    const diagram = this.diagram;

    diagram.diagrams.push(diagramDi);

    return diagramDi;
  }

  /**
   * Draw processes.
   * Root processes should be processed first for element expanding
   */
  drawProcesses() {
    const sortedProcesses = this.layoutedProcesses.sort((a, b) => a.level - b.level);

    for (const process of sortedProcesses) {

      // draw processes in expanded elements
      if (process.isExpanded) {
        const baseProcDi = this.getElementDi(process);
        const diagram = this.getProcDi(baseProcDi);
        let { x, y } = baseProcDi.bounds;
        const { width, height } = getDefaultSize(process);
        x += DEFAULT_CELL_WIDTH / 2 - width / 4;
        y += DEFAULT_CELL_HEIGHT - height - height / 4;
        this.generateDi(process.grid, { x, y }, diagram);
        continue;
      }

      // draw other processes
      const diagram = this.diagram.diagrams.find(diagram => diagram.plane.bpmnElement === process);
      this.generateDi(process.grid, { x: 0, y: 0 }, diagram);
    }
  }

  getElementDi(element) {
    return this.diagram.diagrams
      .map(diagram => diagram.plane.planeElement).flat()
      .find(item => item.bpmnElement === element);
  }

  getProcDi(element) {
    return this.diagram.diagrams.find(diagram => diagram.plane.planeElement.includes(element));
  }
}

function bindBoundaryEventsWithHosts(elements) {
  const boundaryEvents = elements.filter(element => isBoundaryEvent(element));
  boundaryEvents.forEach(boundaryEvent => {
    const attachedTask = boundaryEvent.attachedToRef;
    const attachers = attachedTask.attachers || [];
    attachers.push(boundaryEvent);
    attachedTask.attachers = attachers;
  });
}

/**
 * Check grid by columns.
 * If column has elements with isExpanded === true,
 * find the maximum size of elements grids and expand the parent grid horizontally.
 * @param grid
 */
function expandGridHorizontally(grid) {
  const [ numRows , maxCols ] = grid.getGridDimensions();
  for (let i = maxCols - 1 ; i >= 0; i--) {
    const elementsInCol = [];
    for (let j = 0; j < numRows; j++) {
      const candidate = grid.get(j, i);
      if (candidate && candidate.isExpanded) elementsInCol.push(candidate);
    }

    if (elementsInCol.length === 0) continue;

    const maxColCount = elementsInCol.reduce((acc,cur) => {
      const [ ,curCols ] = cur.grid.getGridDimensions();
      if (acc === undefined || curCols > acc) return curCols;
      return acc;
    }, undefined);

    const shift = !maxColCount ? 2 : maxColCount;
    grid.createCol(i, shift);
  }
}

/**
 * Check grid by rows.
 * If row has elements with isExpanded === true,
 * find the maximum size of elements grids and expand the parent grid vertically.
 * @param grid
 */
function expandGridVertically(grid) {
  const [ numRows , maxCols ] = grid.getGridDimensions();

  for (let i = numRows - 1 ; i >= 0; i--) {
    const elementsInRow = [];
    for (let j = 0; j < maxCols; j++) {
      const candidate = grid.get(i, j);
      if (candidate && candidate.isExpanded) elementsInRow.push(candidate);
    }

    if (elementsInRow.length === 0) continue;

    const maxRowCount = elementsInRow.reduce((acc,cur) => {
      const [ curRows ] = cur.grid.getGridDimensions();
      if (acc === undefined || curRows > acc) return curRows;
      return acc;
    }, undefined);

    const shift = !maxRowCount ? 1 : maxRowCount;

    // expand the parent grid vertically
    for (let index = 0; index < shift; index++) {
      grid.createRow(i);
    }
  }
}

function hasOtherIncoming(element) {
  const fromHost = element.incoming?.filter(edge => edge.sourceRef !== element && edge.sourceRef.attachedToRef === undefined) || [];

  const fromAttached = element.incoming?.filter(edge => edge.sourceRef !== element
      && edge.sourceRef.attachedToRef !== element);

  return fromHost?.length > 0 || fromAttached?.length > 0;
}

function layoutProcess(xml) {
  return new Layouter().layoutProcess(xml);
}

export { layoutProcess };
//# sourceMappingURL=index.js.map
